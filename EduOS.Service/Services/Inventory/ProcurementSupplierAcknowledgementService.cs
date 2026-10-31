using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Inventory;

/// <summary>Supplier response evidence. Only independently approved evidence is a confirmed commitment.</summary>
public sealed class ProcurementSupplierAcknowledgementService : IProcurementSupplierAcknowledgementService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly ILogger<ProcurementSupplierAcknowledgementService> logger;
    public ProcurementSupplierAcknowledgementService(EduOSDbContext db, ICurrentUserService user,
        TimeProvider clock, ILogger<ProcurementSupplierAcknowledgementService> logger)
    { this.db = db; this.user = user; this.clock = clock; this.logger = logger; }

    private bool Reader => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("InventoryManager") ||
         user.IsInRole("StoreKeeper") || user.IsInRole("Accountant"));
    private bool Maker => Reader && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        user.IsInRole("InventoryManager") || user.IsInRole("StoreKeeper"));
    private bool Checker => Reader && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        user.IsInRole("InventoryManager"));
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Supplier confirmation access denied.", 403);
    private static bool Version(byte[] actual, string? supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) &&
            actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private IQueryable<ProcurementSupplierAcknowledgement> Visible() =>
        db.ProcurementSupplierAcknowledgements.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted &&
            db.Campuses.Any(c => c.TenantId == user.TenantId && c.Id == x.CampusId && !c.IsDeleted) &&
            (AllCampuses || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.UserId == user.UserId && a.CampusId == x.CampusId && a.IsActive && !a.IsDeleted)));
    private async Task<bool> CampusAllowed(long campusId, CancellationToken ct) =>
        campusId > 0 && await db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.Id == campusId && x.IsActive && !x.IsDeleted, ct) &&
        (AllCampuses || await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.CampusId == campusId && x.UserId == user.UserId &&
            x.IsActive && !x.IsDeleted, ct));

    private static bool SqlConflict(Exception ex)
    {
        for (var depth = 0; ex != null && depth < 5; ex = ex.InnerException!, depth++)
            if (ex is Microsoft.Data.SqlClient.SqlException sql &&
                sql.Number is 1205 or 1222 or 3960 or 2601 or 2627) return true;
        return false;
    }
    private static bool DateOnly(DateTime date) => date != default && date.TimeOfDay == TimeSpan.Zero;
    private static ProcurementSupplierAcknowledgementDto Map(ProcurementSupplierAcknowledgement x, string orderNumber) => new()
    {
        Id = x.Id, ClientRequestId = x.ClientRequestId, CampusId = x.CampusId,
        PurchaseOrderId = x.PurchaseOrderId, OrderNumber = orderNumber, SupplierId = x.SupplierId,
        SupplierReference = x.SupplierReference, StateCode = x.StateCode, ResponseDate = x.ResponseDate,
        PromisedDeliveryDate = x.PromisedDeliveryDate, SubmittedAt = x.SubmittedAt, DecidedAt = x.DecidedAt,
        DecidedByUserId = x.DecidedByUserId, DecisionReason = x.DecisionReason,
        CreatedBy = x.CreatedBy, CreatedAt = x.CreatedAt, RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private async Task<ProcurementSupplierAcknowledgementDto> MapAsync(
        ProcurementSupplierAcknowledgement row, CancellationToken ct)
    {
        var number = await db.ProcurementPurchaseOrders.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.Id == row.PurchaseOrderId &&
                x.CampusId == row.CampusId && x.SupplierId == row.SupplierId && !x.IsDeleted)
            .Select(x => x.OrderNumber).FirstOrDefaultAsync(ct) ?? string.Empty;
        return Map(row, number);
    }
    public async Task<ApiResponse<PagedResult<ProcurementSupplierAcknowledgementDto>>> ListAsync(
        int page, int pageSize, long? campusId, string? state, CancellationToken ct = default)
    {
        if (!Reader) return Denied<PagedResult<ProcurementSupplierAcknowledgementDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || state?.Length > 20)
            return ApiResponse<PagedResult<ProcurementSupplierAcknowledgementDto>>.ErrorResponse("Invalid filters.");
        var query = Visible();
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.PurchaseOrderId).Distinct().ToArray();
        var numbers = await db.ProcurementPurchaseOrders.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.CampusId, x.SupplierId, x.OrderNumber }).ToListAsync(ct);
        var mapped = rows.Select(x => Map(x, numbers.FirstOrDefault(o =>
            o.Id == x.PurchaseOrderId && o.CampusId == x.CampusId &&
            o.SupplierId == x.SupplierId)?.OrderNumber ?? string.Empty)).ToList();
        return ApiResponse<PagedResult<ProcurementSupplierAcknowledgementDto>>.SuccessResponse(
            new PagedResult<ProcurementSupplierAcknowledgementDto>
            { Page = page, PageSize = pageSize, TotalCount = total, Items = mapped });
    }
    public async Task<ApiResponse<ProcurementSupplierAcknowledgementDto>> GetAsync(
        long id, CancellationToken ct = default)
    {
        if (!Reader) return Denied<ProcurementSupplierAcknowledgementDto>();
        var row = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Confirmation not found.", 404)
            : ApiResponse<ProcurementSupplierAcknowledgementDto>.SuccessResponse(await MapAsync(row, ct));
    }
    public async Task<ApiResponse<ProcurementSupplierAcknowledgementDto>> SaveAsync(
        long? id, SaveProcurementSupplierAcknowledgementRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Denied<ProcurementSupplierAcknowledgementDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.PurchaseOrderId <= 0 ||
            string.IsNullOrWhiteSpace(request.SupplierReference) ||
            request.SupplierReference.Trim().Length is < 3 or > 80 ||
            !DateOnly(request.ResponseDate) || !DateOnly(request.PromisedDeliveryDate) ||
            request.ResponseDate.Date > clock.GetUtcNow().UtcDateTime.Date ||
            request.PromisedDeliveryDate.Date < request.ResponseDate.Date || id is <= 0)
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Invalid supplier confirmation.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var po = await db.ProcurementPurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.PurchaseOrderId && !x.IsDeleted, ct);
            if (po == null || !await CampusAllowed(po.CampusId, ct))
                return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Order not found.", 404);
            if (po.StateCode != "Approved")
                return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Only approved orders can be confirmed.", 409);
            var supplierValid = await db.ProcurementSuppliers.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == po.SupplierId && x.CampusId == po.CampusId &&
                x.StateCode == "Approved" && !x.IsDeleted, ct);
            if (!supplierValid) return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse(
                "Approved supplier required.", 409);
            ProcurementSupplierAcknowledgement row;
            if (id.HasValue)
            {
                var found = await db.ProcurementSupplierAcknowledgements.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == id.Value && !x.IsDeleted, ct);
                if (found == null || !await CampusAllowed(found.CampusId, ct))
                    return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Confirmation not found.", 404);
                if (found.CreatedBy != user.UserId || found.StateCode != "Draft" ||
                    found.PurchaseOrderId != po.Id || found.ClientRequestId != request.ClientRequestId)
                    return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Draft ownership conflict.", 409);
                if (!Version(found.RowVersion, request.RowVersion))
                    return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Confirmation changed; reload.", 409);
                row = found;
            }
            else
            {
                var existing = await db.ProcurementSupplierAcknowledgements.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId &&
                    (x.ClientRequestId == request.ClientRequestId || x.PurchaseOrderId == po.Id), ct);
                if (existing != null)
                {
                    if (!await CampusAllowed(existing.CampusId, ct) || existing.CreatedBy != user.UserId)
                        return Denied<ProcurementSupplierAcknowledgementDto>();
                    return existing.ClientRequestId == request.ClientRequestId &&
                        existing.PurchaseOrderId == po.Id &&
                        existing.SupplierReference == request.SupplierReference.Trim() &&
                        existing.ResponseDate == request.ResponseDate &&
                        existing.PromisedDeliveryDate == request.PromisedDeliveryDate
                        ? ApiResponse<ProcurementSupplierAcknowledgementDto>.SuccessResponse(
                            await MapAsync(existing, ct), "Existing request.")
                        : ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse(
                            "Order already has confirmation or request ID conflict.", 409);
                }
                row = new ProcurementSupplierAcknowledgement
                {
                    TenantId = user.TenantId, ClientRequestId = request.ClientRequestId,
                    CampusId = po.CampusId, PurchaseOrderId = po.Id, SupplierId = po.SupplierId,
                    CreatedAt = clock.GetUtcNow().UtcDateTime, CreatedBy = user.UserId
                };
                db.ProcurementSupplierAcknowledgements.Add(row);
            }
            row.SupplierReference = request.SupplierReference.Trim();
            row.ResponseDate = request.ResponseDate;
            row.PromisedDeliveryDate = request.PromisedDeliveryDate;
            row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            row.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.SuccessResponse(await MapAsync(row, ct));
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Competing update; reload.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (SqlConflict(ex))
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Competing confirmation; retry.", 409);
        }
        catch (InvalidOperationException ex) when (SqlConflict(ex))
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Competing confirmation; retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            logger.LogWarning(ex, "Supplier confirmation integrity conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Duplicate confirmation or invalid reference.", 409);
        }
    }
    public async Task<ApiResponse<ProcurementSupplierAcknowledgementDto>> TransitionAsync(
        long id, string action, ProcurementSupplierAcknowledgementDecisionRequest request, CancellationToken ct = default)
    {
        if (!Reader) return Denied<ProcurementSupplierAcknowledgementDto>();
        if (id <= 0 || request == null || action is not ("submit" or "approve" or "reject" or "revise") ||
            request.Reason?.Length > 500)
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Invalid decision.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var row = await db.ProcurementSupplierAcknowledgements.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
            if (row == null || !await CampusAllowed(row.CampusId, ct))
                return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Confirmation not found.", 404);
            var owner = row.CreatedBy == user.UserId;
            if ((action is "submit" or "revise") && (!Maker || !owner) ||
                (action is "approve" or "reject") && (!Checker || owner))
                return Denied<ProcurementSupplierAcknowledgementDto>();
            if (!Version(row.RowVersion, request.RowVersion))
                return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Confirmation changed; reload.", 409);
            if (!(action switch
            {
                "submit" => row.StateCode == "Draft",
                "approve" or "reject" => row.StateCode == "Submitted",
                "revise" => row.StateCode == "Rejected",
                _ => false
            })) return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Invalid transition.", 409);
            if (action == "reject" && (request.Reason == null ||
                request.Reason.Trim().Length is < 10 or > 500))
                return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Rejection reason required.");
            var po = await db.ProcurementPurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == row.PurchaseOrderId &&
                x.CampusId == row.CampusId && x.SupplierId == row.SupplierId && !x.IsDeleted, ct);
            if (po == null || po.StateCode != "Approved")
                return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Order no longer approved.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            row.StateCode = action switch
            {
                "submit" => "Submitted", "approve" => "Confirmed",
                "reject" => "Rejected", _ => "Draft"
            };
            if (action == "submit")
            { row.SubmittedAt = now; row.DecidedAt = null; row.DecidedByUserId = null; row.DecisionReason = null; }
            if (action == "revise")
            { row.SubmittedAt = null; row.DecidedAt = null; row.DecidedByUserId = null; row.DecisionReason = null; }
            if (action is "approve" or "reject")
            { row.DecidedAt = now; row.DecidedByUserId = user.UserId; row.DecisionReason = request.Reason?.Trim(); }
            row.UpdatedAt = now; row.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.SuccessResponse(await MapAsync(row, ct));
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Competing decision; reload.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (SqlConflict(ex))
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Competing decision; retry.", 409);
        }
        catch (InvalidOperationException ex) when (SqlConflict(ex))
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Competing decision; retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            logger.LogWarning(ex, "Supplier confirmation decision conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<ProcurementSupplierAcknowledgementDto>.ErrorResponse("Decision reference changed.", 409);
        }
    }
}
