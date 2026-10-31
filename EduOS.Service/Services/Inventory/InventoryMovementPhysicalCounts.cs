using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryMovementService
{
    private const decimal MaximumCountQuantity = 999999999999m;

    private static string Fingerprint(object value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private IQueryable<InventoryPhysicalCount> VisibleCounts() => db.InventoryPhysicalCounts.AsNoTracking()
        .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && Locations().Select(l => l.Id).Contains(x.LocationId));

    private static bool ValidQuantity(decimal quantity, bool signed = false) =>
        (signed ? quantity >= -MaximumCountQuantity && quantity <= MaximumCountQuantity :
            quantity >= 0m && quantity <= MaximumCountQuantity) &&
        Precision(quantity, 6);

    private static string StartHash(StartInventoryPhysicalCountRequestDto request, string reason) => Fingerprint(new
    {
        request.LocationId, request.Blind, Reason = reason,
        Lines = request.Lines.OrderBy(x => x.InventoryItemId).ThenBy(x => x.LotOrBatchNo?.Trim() ?? "")
            .Select(x => new { x.InventoryItemId, Lot = x.LotOrBatchNo?.Trim() ?? "" }).ToArray()
    });

    private static string SubmitHash(SubmitInventoryPhysicalCountRequestDto request) => Fingerprint(
        request.Lines.OrderBy(x => x.LineNo).Select(x => new { x.LineNo, x.CountedQuantity }).ToArray());

    private static string ReviewHash(ReviewInventoryPhysicalCountRequestDto request, string reason) => Fingerprint(new
    {
        request.Decision, request.VarianceMode, Reason = reason,
        Corrections = request.Corrections.OrderBy(x => x.LineNo).Select(x => new
        {
            x.LineNo, x.Quantity, x.ReceiptUnitCost, Reason = x.Reason?.Trim() ?? ""
        }).ToArray()
    });

    private async Task<InventoryPhysicalCountDto> CountDtoAsync(InventoryPhysicalCount count, CancellationToken ct)
    {
        var lines = await db.InventoryPhysicalCountLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.InventoryPhysicalCountId == count.Id && !x.IsDeleted)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        var movementIds = lines.Where(x => x.PostedMovementId.HasValue)
            .Select(x => x.PostedMovementId!.Value).ToArray();
        var movementRefs = await db.InventoryMovements.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && movementIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.PublicId }).ToDictionaryAsync(x => x.Id, x => x.PublicId, ct);
        var reveal = !count.Blind || count.StateCode != "Draft";
        return new InventoryPhysicalCountDto
        {
            Reference = count.PublicId, LocationId = count.LocationId, StateCode = count.StateCode,
            Blind = count.Blind, Reason = count.Reason, SnapshotAtUtc = count.SnapshotAtUtc,
            SubmittedAtUtc = count.SubmittedAtUtc, ReviewedAtUtc = count.ReviewedAtUtc,
            CreatedByUserId = count.CreatedBy, ReviewedByUserId = count.ReviewedByUserId,
            VarianceMode = count.VarianceMode, ReviewReason = count.ReviewReason,
            RowVersion = Convert.ToBase64String(count.RowVersion),
            Lines = lines.Select(x => new InventoryPhysicalCountLineDto
            {
                LineNo = x.LineNo, InventoryItemId = x.InventoryItemId, LotOrBatchNo = x.LotOrBatchNo,
                BookQuantity = reveal ? x.BookQuantity : null,
                BookValue = reveal ? x.BookValue : null,
                ReservedQuantity = reveal ? x.ReservedQuantity : null,
                CountedQuantity = x.CountedQuantity,
                SuggestedVariance = reveal && x.CountedQuantity.HasValue ? x.CountedQuantity - x.BookQuantity : null,
                PostedVariance = x.PostedVariance, PostedUnitCost = x.PostedUnitCost,
                CorrectionReason = x.CorrectionReason,
                PostedMovementReference = x.PostedMovementId.HasValue && movementRefs.TryGetValue(x.PostedMovementId.Value, out var reference)
                    ? reference : null
            }).ToList()
        };
    }

    public async Task<ApiResponse<PagedResult<InventoryPhysicalCountDto>>> GetPhysicalCountsAsync(
        long? locationId, string? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId) return Denied<PagedResult<InventoryPhysicalCountDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || locationId < 1 ||
            (!string.IsNullOrWhiteSpace(state) && state is not ("Draft" or "Submitted" or "Posted" or "Rejected")))
            return ApiResponse<PagedResult<InventoryPhysicalCountDto>>.ErrorResponse("Invalid count filters.");
        var query = VisibleCounts();
        if (locationId.HasValue) query = query.Where(x => x.LocationId == locationId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state);
        var total = await query.CountAsync(ct);
        var counts = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var results = new List<InventoryPhysicalCountDto>();
        foreach (var count in counts) results.Add(await CountDtoAsync(count, ct));
        return ApiResponse<PagedResult<InventoryPhysicalCountDto>>.SuccessResponse(new PagedResult<InventoryPhysicalCountDto>
        { Items = results, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<InventoryPhysicalCountDto>> GetPhysicalCountAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Readable || db.CurrentTenantId != user.TenantId) return Denied<InventoryPhysicalCountDto>();
        var count = await VisibleCounts().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        return count == null
            ? ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Physical count not found.", 404)
            : ApiResponse<InventoryPhysicalCountDto>.SuccessResponse(await CountDtoAsync(count, ct));
    }

    public async Task<ApiResponse<InventoryPhysicalCountDto>> StartPhysicalCountAsync(
        StartInventoryPhysicalCountRequestDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryPhysicalCountDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.LocationId <= 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800 ||
            request.Lines is not { Count: >= 1 and <= 100 } ||
            request.Lines.Any(x => x.InventoryItemId <= 0 || x.LotOrBatchNo?.Trim().Length > 100) ||
            request.Lines.Select(x => (x.InventoryItemId, x.LotOrBatchNo?.Trim() ?? "")).Distinct().Count() != request.Lines.Count)
            return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Location, unique item/lot lines and reason (10-800 characters) required.");
        var reason = request.Reason.Trim();
        var hash = StartHash(request, reason);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.InventoryPhysicalCounts.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, ct);
            if (existing != null)
            {
                if (existing.StartFingerprint != hash) return CountConflict("Request ID already used for different count.");
                if (!await LocationsAllowed(existing.LocationId, null, ct)) return Denied<InventoryPhysicalCountDto>();
                return ApiResponse<InventoryPhysicalCountDto>.SuccessResponse(await CountDtoAsync(existing, ct), "Existing count returned.");
            }
            if (!await LocationsAllowed(request.LocationId, null, ct)) return Denied<InventoryPhysicalCountDto>();
            var itemIds = request.Lines.Select(x => x.InventoryItemId).Distinct().ToArray();
            if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                itemIds.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != itemIds.Length)
                return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Stock item missing or inactive.", 404);
            var snapshot = await Balances(itemIds, [request.LocationId], null, ct);
            var reserved = await ReservedQuantitiesAsync(itemIds, [request.LocationId], null, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var count = new InventoryPhysicalCount
            {
                TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                LocationId = request.LocationId, Blind = request.Blind, Reason = reason,
                StartFingerprint = hash, SnapshotAtUtc = now, CreatedBy = user.UserId, CreatedAt = now
            };
            db.InventoryPhysicalCounts.Add(count);
            await db.SaveChangesAsync(ct);
            var lines = request.Lines.Select((selection, index) =>
            {
                var lot = selection.LotOrBatchNo?.Trim() ?? "";
                var value = Get(snapshot, selection.InventoryItemId, request.LocationId, lot);
                return new InventoryPhysicalCountLine
                {
                    TenantId = user.TenantId, InventoryPhysicalCountId = count.Id, LineNo = index + 1,
                    InventoryItemId = selection.InventoryItemId, LotOrBatchNo = lot,
                    BookQuantity = value.Quantity, BookValue = decimal.Round(value.Value, 4),
                    ReservedQuantity = reserved.GetValueOrDefault((selection.InventoryItemId, request.LocationId, lot)),
                    CreatedAt = now, CreatedBy = user.UserId
                };
            }).ToArray();
            db.InventoryPhysicalCountLines.AddRange(lines);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryPhysicalCountDto>.SuccessResponse(await CountDtoAsync(count, ct), "Physical count opened.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Physical count create race"); return CountConflict("Count create conflict; reload."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        { logger.LogWarning(ex, "Physical count create SQL race"); return CountConflict("Count create conflict; reload."); }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        { logger.LogWarning(ex, "Physical count create wrapped SQL race"); return CountConflict("Count create conflict; reload."); }
    }

    private static ApiResponse<InventoryPhysicalCountDto> CountConflict(string message) =>
        ApiResponse<InventoryPhysicalCountDto>.ErrorResponse(message, 409);

    public async Task<ApiResponse<InventoryPhysicalCountDto>> SubmitPhysicalCountAsync(
        Guid reference, SubmitInventoryPhysicalCountRequestDto request, CancellationToken ct = default)
    {
        if (!Writable || db.CurrentTenantId != user.TenantId) return Denied<InventoryPhysicalCountDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.Lines is not { Count: >= 1 and <= 100 } ||
            request.Lines.Any(x => x.LineNo <= 0 || x.CountedQuantity == null || !ValidQuantity(x.CountedQuantity.Value)) ||
            request.Lines.Select(x => x.LineNo).Distinct().Count() != request.Lines.Count)
            return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Every line needs an explicit nonnegative counted quantity (zero is allowed).");
        var hash = SubmitHash(request);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var count = await db.InventoryPhysicalCounts.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted, ct);
            if (count == null || !await LocationsAllowed(count.LocationId, null, ct))
                return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Count not found.", 404);
            if (count.SubmitRequestId == request.ClientRequestId && count.SubmitFingerprint == hash)
                return ApiResponse<InventoryPhysicalCountDto>.SuccessResponse(await CountDtoAsync(count, ct), "Already submitted.");
            if (count.StateCode != "Draft" || !Version(count.RowVersion, request.RowVersion))
                return CountConflict("Count changed; reload before submission.");
            if (count.CreatedBy != user.UserId) return Denied<InventoryPhysicalCountDto>();
            var lines = await db.InventoryPhysicalCountLines.Where(x => x.TenantId == user.TenantId &&
                x.InventoryPhysicalCountId == count.Id && !x.IsDeleted).ToListAsync(ct);
            var supplied = request.Lines.ToDictionary(x => x.LineNo);
            if (lines.Count != supplied.Count || lines.Any(x => !supplied.ContainsKey(x.LineNo)))
                return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("All snapshot lines must be measured.");
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var line in lines) line.CountedQuantity = supplied[line.LineNo].CountedQuantity;
            count.SubmitRequestId = request.ClientRequestId; count.SubmitFingerprint = hash;
            count.StateCode = "Submitted"; count.SubmittedAtUtc = now; count.SubmittedByUserId = user.UserId;
            count.UpdatedAt = now; count.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryPhysicalCountDto>.SuccessResponse(await CountDtoAsync(count, ct), "Count submitted for independent review.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Physical count submit race"); return CountConflict("Count submit conflict; reload."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        { logger.LogWarning(ex, "Physical count submit SQL race"); return CountConflict("Count submit conflict; reload."); }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        { logger.LogWarning(ex, "Physical count submit wrapped SQL race"); return CountConflict("Count submit conflict; reload."); }
    }

    public async Task<ApiResponse<InventoryPhysicalCountDto>> ReviewPhysicalCountAsync(
        Guid reference, ReviewInventoryPhysicalCountRequestDto request, CancellationToken ct = default)
    {
        if (!Postable || db.CurrentTenantId != user.TenantId) return Denied<InventoryPhysicalCountDto>();
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.Decision is not ("Approve" or "Reject") ||
            request.VarianceMode is not ("Automatic" or "ManualFinal" or "AutoPlusManual") ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 800 ||
            request.Corrections == null || request.Corrections.Count > 100 ||
            request.Corrections.Any(x => x.LineNo <= 0 || !ValidQuantity(x.Quantity, true) ||
                (x.ReceiptUnitCost.HasValue && (x.ReceiptUnitCost < 0m ||
                    x.ReceiptUnitCost > MaximumCountQuantity || !Precision(x.ReceiptUnitCost.Value, 4))) ||
                x.Reason?.Trim().Length > 800) ||
            request.Corrections.Select(x => x.LineNo).Distinct().Count() != request.Corrections.Count ||
            (request.Decision == "Reject" && request.Corrections.Count != 0) ||
            (request.Decision == "Approve" && request.VarianceMode == "Automatic" &&
                request.Corrections.Any(x => x.Quantity != 0m || !x.ReceiptUnitCost.HasValue)))
            return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Decision, variance policy, signed corrections and reason (10-800 characters) required.");
        var reason = request.Reason.Trim();
        var hash = ReviewHash(request, reason);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var count = await db.InventoryPhysicalCounts.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == reference && !x.IsDeleted, ct);
            if (count == null || !await LocationsAllowed(count.LocationId, null, ct))
                return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Count not found.", 404);
            if (count.ReviewRequestId == request.ClientRequestId && count.ReviewFingerprint == hash)
                return ApiResponse<InventoryPhysicalCountDto>.SuccessResponse(await CountDtoAsync(count, ct), "Already reviewed.");
            if (count.StateCode != "Submitted" || !Version(count.RowVersion, request.RowVersion))
                return CountConflict("Count changed or already reviewed.");
            if (!count.CreatedBy.HasValue || count.CreatedBy == user.UserId || count.SubmittedByUserId == user.UserId)
                return Denied<InventoryPhysicalCountDto>();
            var lines = await db.InventoryPhysicalCountLines.Where(x => x.TenantId == user.TenantId &&
                x.InventoryPhysicalCountId == count.Id && !x.IsDeleted).OrderBy(x => x.LineNo).ToListAsync(ct);
            if (lines.Count == 0 || lines.Any(x => !x.CountedQuantity.HasValue))
                return CountConflict("Incomplete submitted count.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (request.Decision == "Approve")
            {
                var itemIds = lines.Select(x => x.InventoryItemId).Distinct().ToArray();
                if (!await LocationsAllowed(count.LocationId, null, ct) ||
                    await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                        itemIds.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != itemIds.Length)
                    return CountConflict("Count location/items are no longer active.");
                var balances = await Balances(itemIds, [count.LocationId], null, ct);
                foreach (var line in lines)
                {
                    var current = Get(balances, line.InventoryItemId, count.LocationId, line.LotOrBatchNo);
                    if (current.Quantity != line.BookQuantity || decimal.Round(current.Value, 4) != line.BookValue)
                        return CountConflict("Stock changed since snapshot; start a new count.");
                }
                // A receipt and issue that net to zero still invalidate the historical snapshot.
                var movementsSinceSnapshot = await (from m in db.InventoryMovements.AsNoTracking()
                    join l in db.InventoryMovementLines.AsNoTracking() on m.Id equals l.InventoryMovementId
                    where m.TenantId == user.TenantId && l.TenantId == user.TenantId &&
                        !m.IsDeleted && !l.IsDeleted && m.StateCode == "Posted" && m.PostedAt > count.SnapshotAtUtc &&
                        (m.FromLocationId == count.LocationId || m.ToLocationId == count.LocationId) &&
                        itemIds.Contains(l.InventoryItemId)
                    select new { l.InventoryItemId, Lot = l.LotOrBatchNo }).ToListAsync(ct);
                if (movementsSinceSnapshot.Any(x => lines.Any(l => l.InventoryItemId == x.InventoryItemId &&
                    l.LotOrBatchNo == (x.Lot ?? ""))))
                    return CountConflict("Stock ledger changed after snapshot; recount required.");

                var corrections = request.Corrections.ToDictionary(x => x.LineNo);
                if (corrections.Keys.Any(lineNo => lines.All(x => x.LineNo != lineNo)) ||
                    (request.VarianceMode == "ManualFinal" && corrections.Count != lines.Count))
                    return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Corrections must reference every counted line for Manual Final, or existing lines for Auto + Manual.");
                foreach (var line in lines)
                {
                    var suggested = line.CountedQuantity!.Value - line.BookQuantity;
                    corrections.TryGetValue(line.LineNo, out var correction);
                    var variance = request.VarianceMode switch
                    {
                        "Automatic" => suggested,
                        "ManualFinal" => correction!.Quantity,
                        _ => suggested + (correction?.Quantity ?? 0m)
                    };
                    if (!ValidQuantity(variance, true) || line.BookQuantity + variance < 0m)
                        return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Final variance exceeds stock limits or makes stock negative.");
                    var avg = line.BookQuantity > 0m
                        ? decimal.Round(line.BookValue / line.BookQuantity, 4, MidpointRounding.ToZero) : 0m;
                    var requestedUnitCost = correction?.ReceiptUnitCost;
                    var unitCost = variance > 0m && requestedUnitCost is > 0m
                        ? requestedUnitCost.Value : avg;
                    var costOverride = variance > 0m && requestedUnitCost is > 0m &&
                        requestedUnitCost.Value != avg;
                    if ((variance != suggested || costOverride) &&
                        (correction == null || string.IsNullOrWhiteSpace(correction.Reason) ||
                            correction.Reason.Trim().Length is < 10 or > 800))
                        return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Every quantity or valuation override needs a line reason (10-800 characters).");
                    if (variance > 0m && unitCost <= 0m)
                        return ApiResponse<InventoryPhysicalCountDto>.ErrorResponse("Positive variance without book valuation requires receipt unit cost.");
                    line.PostedVariance = variance;
                    line.CorrectionReason = variance == suggested && !costOverride ? null : correction!.Reason!.Trim();
                    if (variance == 0m) continue;

                    var receipt = variance > 0m;
                    var draft = new CreateInventoryMovementRequestDto
                    {
                        ClientRequestId = Guid.NewGuid(), MovementTypeCode = receipt ? "Receipt" : "Issue",
                        ToLocationId = receipt ? count.LocationId : null,
                        FromLocationId = receipt ? null : count.LocationId,
                        SourceType = "PhysicalStockCount",
                        Remarks = "Physical count " + count.PublicId + "; " + reason,
                        Lines = [new CreateInventoryMovementLineRequestDto
                        {
                            InventoryItemId = line.InventoryItemId, LotOrBatchNo = line.LotOrBatchNo,
                            Quantity = Math.Abs(variance), UnitCost = receipt ? unitCost : 0m
                        }]
                    };
                    var (movement, _) = await PersistDraftAsync(draft, ct);
                    movement.SourceId = count.Id;
                    var (error, postedLines) = await CheckPostingAsync(movement, ct);
                    if (error != null) return CountConflict("Variance cannot post: " + error);
                    movement.StateCode = "Posted"; movement.PostedAt = now; movement.PostedByUserId = user.UserId;
                    movement.UpdatedAt = now; movement.UpdatedBy = user.UserId;
                    line.PostedMovementId = movement.Id;
                    line.PostedUnitCost = postedLines[0].UnitCost;
                    await db.SaveChangesAsync(ct);
                }
            }
            count.StateCode = request.Decision == "Approve" ? "Posted" : "Rejected";
            count.ReviewRequestId = request.ClientRequestId; count.ReviewFingerprint = hash;
            count.VarianceMode = request.Decision == "Approve" ? request.VarianceMode : null;
            count.ReviewReason = reason; count.ReviewedAtUtc = now; count.ReviewedByUserId = user.UserId;
            count.UpdatedAt = now; count.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryPhysicalCountDto>.SuccessResponse(await CountDtoAsync(count, ct),
                request.Decision == "Approve" ? "Physical count approved; variances posted atomically." : "Physical count rejected; stock unchanged.");
        }
        catch (DbUpdateException ex) { logger.LogWarning(ex, "Physical count review race"); return CountConflict("Count review conflict; reload."); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        { logger.LogWarning(ex, "Physical count review SQL race"); return CountConflict("Count review conflict; reload."); }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        { logger.LogWarning(ex, "Physical count review wrapped SQL race"); return CountConflict("Count review conflict; reload."); }
    }
}
