using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Fulfils a reserved manual issue through a new immutable posted issue in the
/// canonical movement ledger. The reserved parent retains its requested lines;
/// FulfilledQuantity tracks consumed quantities; requested minus fulfilled is held.
/// </summary>
public sealed partial class InventoryMovementService
{
    public Task<ApiResponse<InventoryMovementDto>> FulfillReservedIssueAsync(
        Guid movementReference, FulfillInventoryReservedIssueRequestDto request, CancellationToken ct = default)
        => FulfillReservedMovementAsync(movementReference, request, "Issue", "ManualIssue",
            "ReservationIssueFulfillment", ct);

    public Task<ApiResponse<InventoryMovementDto>> FulfillReservedTransferAsync(
        Guid movementReference, FulfillInventoryReservedTransferRequestDto request, CancellationToken ct = default)
        => FulfillReservedMovementAsync(movementReference, request, "Transfer", "ManualTransfer",
            "TransferDispatch", ct);

    private async Task<ApiResponse<InventoryMovementDto>> FulfillReservedMovementAsync(
        Guid movementReference, FulfillInventoryReservedIssueRequestDto request,
        string parentType, string parentSource, string childSource, CancellationToken ct)
    {
        if (!Postable || db.CurrentTenantId != user.TenantId) return Denied<InventoryMovementDto>();
        if (movementReference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.Lines == null ||
            request.Lines.Count is < 1 or > 100 ||
            request.Lines.Any(x => x.LineNo <= 0 || x.Quantity <= 0m ||
                x.Quantity > 999999999999m || !Precision(x.Quantity, 6)) ||
            request.Lines.Select(x => x.LineNo).Distinct().Count() != request.Lines.Count)
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Unique request, row version and valid line quantities required.", 400);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var parent = await db.InventoryMovements.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == movementReference && !x.IsDeleted, ct);
            if (parent == null || !await LocationsAllowed(parent.FromLocationId, parent.ToLocationId, ct))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation not found.", 404);

            // A retry of a committed command returns its exact posted evidence even
            // after the parent rowversion/state has advanced. A reused key with
            // different content is always rejected.
            var prior = await db.InventoryMovements.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (prior != null)
            {
                if (prior.SourceType != childSource ||
                    prior.SourceId != parent.Id || prior.StateCode != "Posted" || prior.IsDeleted)
                    return ApiResponse<InventoryMovementDto>.ErrorResponse("Request ID belongs to another operation.", 409);
                if (await db.InventoryMovements.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.ReversalOfMovementId == prior.Id &&
                    x.StateCode == "Posted" && !x.IsDeleted, ct))
                    return ApiResponse<InventoryMovementDto>.ErrorResponse("Fulfilment was reversed; correction requires a new authorized reservation.", 409);
                var old = await db.InventoryMovementLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.InventoryMovementId == prior.Id && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).ToListAsync(ct);
                var requested = request.Lines.ToDictionary(x => x.LineNo, x => x.Quantity);
                if (old.Count != requested.Count ||
                    old.Any(x => !int.TryParse(x.Remarks, out var originalLine) ||
                        !requested.TryGetValue(originalLine, out var quantity) || x.Quantity != quantity))
                    return ApiResponse<InventoryMovementDto>.ErrorResponse("Request ID has different fulfilment lines.", 409);
                return ApiResponse<InventoryMovementDto>.SuccessResponse(await MapAsync(prior, ct), "Existing fulfilment returned.");
            }

            if (parent.StateCode != "Reserved" || parent.MovementTypeCode != parentType ||
                parent.SourceType != parentSource || !parent.FromLocationId.HasValue ||
                (parentType == "Transfer" && !parent.ToLocationId.HasValue) ||
                !Version(parent.RowVersion, request.RowVersion))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation changed or is not a manual issue.", 409);

            if (parent.ReservationExpiresAtUtc.HasValue &&
                parent.ReservationExpiresAtUtc.Value <= clock.GetUtcNow().UtcDateTime)
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation deadline passed; remaining stock cannot be fulfilled.", 409);

            var lines = await db.InventoryMovementLines.Where(x => x.TenantId == user.TenantId &&
                x.InventoryMovementId == parent.Id && !x.IsDeleted)
                .OrderBy(x => x.LineNo).ToListAsync(ct);
            if (lines.Count is < 1 or > 100 ||
                lines.Any(x => x.FulfilledQuantity < 0m || x.FulfilledQuantity > x.Quantity))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Reservation requires reconciliation.", 409);
            var byNumber = lines.ToDictionary(x => x.LineNo);
            foreach (var part in request.Lines)
                if (!byNumber.TryGetValue(part.LineNo, out var original) ||
                    part.Quantity > original.Quantity - original.FulfilledQuantity)
                    return ApiResponse<InventoryMovementDto>.ErrorResponse("Fulfilment exceeds the reserved remainder.", 409);

            var items = request.Lines.Select(x => byNumber[x.LineNo].InventoryItemId).Distinct().ToArray();
            if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                    items.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != items.Length)
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Inactive or missing stock item.", 409);
            var origin = parent.FromLocationId.Value;
            var balances = await Balances(items, new[] { origin }, null, ct);
            if (!Nonnegative(balances.Values))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Stock ledger requires reconciliation.", 409);
            var reserved = await ReservedQuantitiesAsync(items, new[] { origin }, null, ct);
            var groups = request.Lines.GroupBy(x =>
                (Item: byNumber[x.LineNo].InventoryItemId, Lot: byNumber[x.LineNo].LotOrBatchNo ?? ""))
                .Select(x => new { x.Key.Item, x.Key.Lot, Quantity = x.Sum(y => y.Quantity) }).ToArray();
            var costs = new Dictionary<(long Item, string Lot), decimal>();
            foreach (var group in groups)
            {
                var balance = Get(balances, group.Item, origin, group.Lot);
                var key = (group.Item, origin, group.Lot);
                var held = reserved.GetValueOrDefault(key);
                if (balance.Quantity <= 0m || balance.Quantity < held || held < group.Quantity ||
                    balance.Quantity < group.Quantity)
                    return ApiResponse<InventoryMovementDto>.ErrorResponse("Insufficient reserved item/lot stock.", 409);
                var cost = decimal.Round(balance.Value / balance.Quantity, 4, MidpointRounding.ToZero);
                costs[(group.Item, group.Lot)] = cost;
                Add(balance, -group.Quantity, cost);
                if (balance.Quantity < held - group.Quantity)
                    return ApiResponse<InventoryMovementDto>.ErrorResponse("Fulfilment would consume another reservation.", 409);
            }
            if (!Nonnegative(balances.Values))
                return ApiResponse<InventoryMovementDto>.ErrorResponse("Fulfilment would create invalid stock valuation.", 409);

            var now = clock.GetUtcNow().UtcDateTime;
            var publicId = Guid.NewGuid();
            var posted = new InventoryMovement
            {
                TenantId = user.TenantId, PublicId = publicId, ClientRequestId = request.ClientRequestId,
                MovementNumber = "IM-" + publicId.ToString("N").ToUpperInvariant(),
                MovementTypeCode = "Issue", FromLocationId = origin,
                ToLocationId = parentType == "Transfer" ? parent.ToLocationId : null,
                SourceType = childSource, SourceId = parent.Id,
                MovementAt = now, StateCode = "Posted", PostedAt = now, PostedByUserId = user.UserId,
                Remarks = (parentType == "Transfer" ? "Partial transfer dispatch from reservation " :
                    "Partial issue from reservation ") + parent.MovementNumber,
                CreatedAt = now, CreatedBy = user.UserId
            };
            foreach (var part in request.Lines)
            {
                var original = byNumber[part.LineNo];
                original.FulfilledQuantity += part.Quantity;
                original.UpdatedAt = now;
                original.UpdatedBy = user.UserId;
            }
            parent.StateCode = lines.All(x => x.FulfilledQuantity == x.Quantity) ? "Fulfilled" : "Reserved";
            parent.UpdatedAt = now;
            parent.UpdatedBy = user.UserId;
            db.InventoryMovements.Add(posted);
            await db.SaveChangesAsync(ct);
            var copies = request.Lines.OrderBy(x => x.LineNo).Select((part, i) =>
            {
                var original = byNumber[part.LineNo];
                return new InventoryMovementLine
                {
                    TenantId = user.TenantId, InventoryMovementId = posted.Id, LineNo = i + 1,
                    InventoryItemId = original.InventoryItemId, Quantity = part.Quantity,
                    UnitCost = costs[(original.InventoryItemId, original.LotOrBatchNo ?? "")],
                    LotOrBatchNo = original.LotOrBatchNo,
                    Remarks = part.LineNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CreatedAt = now, CreatedBy = user.UserId
                };
            }).ToList();
            db.InventoryMovementLines.AddRange(copies);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<InventoryMovementDto>.SuccessResponse(Map(posted, copies), parentType == "Transfer" ? "Reserved transfer dispatched." : "Reserved issue fulfilled.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Inventory reserved issue fulfilment conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Fulfilment conflict; reload.", 409);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 2601 or 2627 or 3960)
        {
            logger.LogWarning(ex, "Inventory reserved issue SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Fulfilment conflict; reload.", 409);
        }
        catch (InvalidOperationException ex) when (IsSqlReceiptConflict(ex))
        {
            logger.LogWarning(ex, "Inventory reserved issue wrapped SQL conflict");
            return ApiResponse<InventoryMovementDto>.ErrorResponse("Fulfilment conflict; reload.", 409);
        }
    }
}
