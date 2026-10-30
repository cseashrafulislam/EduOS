using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Maker-checker purchase requisitions. Approved requests authorize later PO work,
/// but NEVER post stock or finance by themselves.
/// </summary>
public sealed class ProcurementRequisitionService : IProcurementRequisitionService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly ILogger<ProcurementRequisitionService> logger;

    public ProcurementRequisitionService(EduOSDbContext db, ICurrentUserService user,
        TimeProvider clock, ILogger<ProcurementRequisitionService> logger)
    { this.db = db; this.user = user; this.clock = clock; this.logger = logger; }

    private bool Reader => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("InventoryManager") ||
         user.IsInRole("StoreKeeper") || user.IsInRole("Accountant"));
    private bool Maker => Reader && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        user.IsInRole("InventoryManager") || user.IsInRole("StoreKeeper"));
    private bool Checker => Reader && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        user.IsInRole("InventoryManager"));
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Procurement access denied.", 403);
    private static bool Version(byte[] current, string? supplied)
    { try { return !string.IsNullOrWhiteSpace(supplied) && current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
      catch (FormatException) { return false; } }
    private static bool Precision(decimal value, int digits) => value == decimal.Round(value, digits);
    private IQueryable<ProcurementRequisition> Visible() => db.ProcurementRequisitions.AsNoTracking()
        .Where(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            db.Campuses.Any(c => c.TenantId == user.TenantId && c.Id == x.CampusId && !c.IsDeleted) &&
            (AllCampuses || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.UserId == user.UserId && a.CampusId == x.CampusId && a.IsActive && !a.IsDeleted)));
    private async Task<bool> CampusAllowed(long campusId, CancellationToken ct)
    {
        if (campusId <= 0 || !await db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.Id == campusId && x.IsActive && !x.IsDeleted, ct))
            return false;
        return AllCampuses || await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.UserId == user.UserId && x.CampusId == campusId &&
            x.IsActive && !x.IsDeleted, ct);
    }
    private static string? Validate(SaveProcurementRequisitionRequest? r)
    {
        if (r == null || r.ClientRequestId == Guid.Empty || r.CampusId <= 0 ||
            string.IsNullOrWhiteSpace(r.Purpose) || r.Purpose.Trim().Length is < 10 or > 500 ||
            r.Lines == null || r.Lines.Count is < 1 or > 100)
            return "Campus, unique request ID, purpose (10-500 characters) and 1-100 lines are required.";
        if (r.Lines.Select(x => x.InventoryItemId).Distinct().Count() != r.Lines.Count ||
            r.Lines.Any(x => x.InventoryItemId <= 0 || x.Quantity <= 0 || x.Quantity > 999999999999m ||
                !Precision(x.Quantity, 6) || x.EstimatedUnitPrice < 0 || x.EstimatedUnitPrice > 999999999999m ||
                !Precision(x.EstimatedUnitPrice, 4) || x.Remarks?.Trim().Length > 300))
            return "Lines require unique valid items, positive quantities and valid decimal prices.";
        return null;
    }
    private static ProcurementRequisitionDto Map(ProcurementRequisition r,
        IEnumerable<ProcurementRequisitionLine> lines)
    {
        var mapped = lines.OrderBy(x => x.LineNo).Select(x => new ProcurementRequisitionLineDto
        {
            LineNo = x.LineNo, InventoryItemId = x.InventoryItemId,
            Quantity = x.Quantity, EstimatedUnitPrice = x.EstimatedUnitPrice, Remarks = x.Remarks
        }).ToList();
        return new ProcurementRequisitionDto
        {
            Id = r.Id, PublicId = r.PublicId, ClientRequestId = r.ClientRequestId, RequestNumber = r.RequestNumber,
            CampusId = r.CampusId, Purpose = r.Purpose, StateCode = r.StateCode,
            CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, SubmittedAt = r.SubmittedAt,
            DecidedAt = r.DecidedAt, DecidedByUserId = r.DecidedByUserId,
            DecisionReason = r.DecisionReason, RowVersion = Convert.ToBase64String(r.RowVersion),
            Lines = mapped, EstimatedTotal = mapped.Sum(x => x.Quantity * x.EstimatedUnitPrice)
        };
    }
    private async Task<ProcurementRequisitionDto> MapAsync(ProcurementRequisition r, CancellationToken ct)
    {
        var lines = await db.ProcurementRequisitionLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.RequisitionId == r.Id && !x.IsDeleted)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        return Map(r, lines);
    }
    public async Task<ApiResponse<PagedResult<ProcurementRequisitionDto>>> ListAsync(
        int page, int pageSize, long? campusId, string? state, string? search, CancellationToken ct = default)
    {
        if (!Reader) return Denied<PagedResult<ProcurementRequisitionDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || campusId is <= 0 ||
            search?.Length > 100 || state?.Length > 20)
            return ApiResponse<PagedResult<ProcurementRequisitionDto>>.ErrorResponse("Invalid filters.");
        var query = Visible();
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state);
        if (!string.IsNullOrWhiteSpace(search))
        { var term = search.Trim(); query = query.Where(x => x.RequestNumber.Contains(term) || x.Purpose.Contains(term)); }
        var total = await query.CountAsync(ct);
        var headers = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = headers.Select(x => x.Id).ToArray();
        var lines = await db.ProcurementRequisitionLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && ids.Contains(x.RequisitionId) && !x.IsDeleted).ToListAsync(ct);
        var grouped = lines.GroupBy(x => x.RequisitionId).ToDictionary(x => x.Key, x => x.AsEnumerable());
        var result = new PagedResult<ProcurementRequisitionDto>
        {
            TotalCount = total, Page = page, PageSize = pageSize,
            Items = headers.Select(x => Map(x, grouped.GetValueOrDefault(x.Id) ??
                Enumerable.Empty<ProcurementRequisitionLine>())).ToList()
        };
        return ApiResponse<PagedResult<ProcurementRequisitionDto>>.SuccessResponse(result);
    }
    public async Task<ApiResponse<ProcurementRequisitionDto>> GetAsync(long id, CancellationToken ct = default)
    {
        if (!Reader) return Denied<ProcurementRequisitionDto>();
        var header = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        return header == null
            ? ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Requisition not found.", 404)
            : ApiResponse<ProcurementRequisitionDto>.SuccessResponse(await MapAsync(header, ct));
    }
    public async Task<ApiResponse<ProcurementRequisitionDto>> SaveAsync(
        long? id, SaveProcurementRequisitionRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Denied<ProcurementRequisitionDto>();
        var error = Validate(request);
        if (error != null || id is <= 0) return ApiResponse<ProcurementRequisitionDto>.ErrorResponse(error ?? "Invalid ID.");
        if (!await CampusAllowed(request.CampusId, ct)) return Denied<ProcurementRequisitionDto>();
        var itemIds = request.Lines.Select(x => x.InventoryItemId).ToArray();
        if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
            itemIds.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != itemIds.Length)
            return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Invalid or inactive stock item.", 400);
        var tenant = user.TenantId;
        ProcurementRequisition? header;
        if (id.HasValue)
        {
            header = await db.ProcurementRequisitions.FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id.Value && !x.IsDeleted, ct);
            if (header == null || !await CampusAllowed(header.CampusId, ct))
                return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Requisition not found.", 404);
            if (header.CreatedBy != user.UserId || header.StateCode != "Draft" ||
                header.CampusId != request.CampusId || header.ClientRequestId != request.ClientRequestId)
                return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Only the maker may edit an unchanged draft.", 409);
            if (!Version(header.RowVersion, request.RowVersion))
                return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Requisition changed; reload.", 409);
        }
        else
        {
            var existing = await db.ProcurementRequisitions.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
            {
                if (!await CampusAllowed(existing.CampusId, ct) || existing.CreatedBy != user.UserId)
                    return Denied<ProcurementRequisitionDto>();
                var previous = await MapAsync(existing, ct);
                var same = existing.CampusId == request.CampusId && existing.Purpose == request.Purpose.Trim() &&
                    previous.Lines.Count == request.Lines.Count &&
                    previous.Lines.Zip(request.Lines).All(x =>
                        x.First.InventoryItemId == x.Second.InventoryItemId &&
                        x.First.Quantity == x.Second.Quantity &&
                        x.First.EstimatedUnitPrice == x.Second.EstimatedUnitPrice &&
                        x.First.Remarks == x.Second.Remarks?.Trim());
                return same ? ApiResponse<ProcurementRequisitionDto>.SuccessResponse(previous, "Existing request.")
                    : ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Request ID was used for different details.", 409);
            }
            var publicId = Guid.NewGuid();
            header = new ProcurementRequisition
            {
                TenantId = tenant, PublicId = publicId, ClientRequestId = request.ClientRequestId,
                RequestNumber = "PR-" + publicId.ToString("N").ToUpperInvariant(),
                CampusId = request.CampusId, CreatedBy = user.UserId, CreatedAt = clock.GetUtcNow().UtcDateTime
            };
            db.ProcurementRequisitions.Add(header);
        }
        header.Purpose = request.Purpose.Trim();
        header.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        header.UpdatedBy = user.UserId;
        // SQL uses one atomic transaction for header, child revisions and audit writes.
        await using IDbContextTransaction? tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.SaveChangesAsync(ct);
            var existingLines = await db.ProcurementRequisitionLines.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenant && x.RequisitionId == header.Id).ToListAsync(ct);
            for (var i = 0; i < request.Lines.Count; i++)
            {
                var input = request.Lines[i];
                var line = existingLines.FirstOrDefault(x => x.LineNo == i + 1);
                if (line == null)
                {
                    line = new ProcurementRequisitionLine
                    {
                        TenantId = tenant, RequisitionId = header.Id, LineNo = i + 1,
                        CreatedAt = clock.GetUtcNow().UtcDateTime, CreatedBy = user.UserId
                    };
                    db.ProcurementRequisitionLines.Add(line);
                }
                line.IsDeleted = false; line.InventoryItemId = input.InventoryItemId;
                line.Quantity = input.Quantity; line.EstimatedUnitPrice = input.EstimatedUnitPrice;
                line.Remarks = input.Remarks?.Trim();
            }
            foreach (var line in existingLines.Where(x => x.LineNo > request.Lines.Count))
                line.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<ProcurementRequisitionDto>.SuccessResponse(await MapAsync(header, ct));
        }
        catch (DbUpdateConcurrencyException)
        { if (tx != null) await tx.RollbackAsync(ct); return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Requisition changed; reload.", 409); }
        catch (DbUpdateException ex)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            logger.LogWarning(ex, "Procurement request integrity conflict for tenant {TenantId}", tenant);
            return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Requisition conflicts with an existing request or item.", 409);
        }
    }
    public async Task<ApiResponse<ProcurementRequisitionDto>> TransitionAsync(
        long id, string action, ProcurementDecisionRequest request, CancellationToken ct = default)
    {
        if (!Reader) return Denied<ProcurementRequisitionDto>();
        if (request == null || id <= 0 || action is not ("submit" or "approve" or "reject" or "cancel") ||
            request.Reason?.Length > 500)
            return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Invalid decision.");
        var header = await db.ProcurementRequisitions.FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (header == null || !await CampusAllowed(header.CampusId, ct))
            return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Requisition not found.", 404);
        var isMaker = header.CreatedBy == user.UserId;
        if ((action is "submit" or "cancel" && (!Maker || !isMaker)) ||
            (action is "approve" or "reject" && (!Checker || isMaker)))
            return Denied<ProcurementRequisitionDto>();
        if (!Version(header.RowVersion, request.RowVersion))
            return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Requisition changed; reload.", 409);
        var allowed = action switch
        {
            "submit" => header.StateCode == "Draft",
            "cancel" => header.StateCode is "Draft" or "Submitted",
            "approve" or "reject" => header.StateCode == "Submitted",
            _ => false
        };
        if (!allowed) return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Invalid state transition.", 409);
        if (action == "reject" && string.IsNullOrWhiteSpace(request.Reason))
            return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Rejection reason is required.");
        header.StateCode = action switch { "submit" => "Submitted", "approve" => "Approved",
            "reject" => "Rejected", _ => "Cancelled" };
        var now = clock.GetUtcNow().UtcDateTime;
        if (action == "submit") header.SubmittedAt = now;
        if (action is "approve" or "reject")
        { header.DecidedAt = now; header.DecidedByUserId = user.UserId; header.DecisionReason = request.Reason?.Trim(); }
        header.UpdatedAt = now; header.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<ProcurementRequisitionDto>.SuccessResponse(await MapAsync(header, ct));
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<ProcurementRequisitionDto>.ErrorResponse("Competing decision; reload.", 409); }
    }
}
