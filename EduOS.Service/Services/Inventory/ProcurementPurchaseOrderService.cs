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

/// <summary>Source-document purchase orders. No inventory or financial posting occurs here.</summary>
public sealed partial class ProcurementPurchaseOrderService : IProcurementPurchaseOrderService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    private readonly ILogger<ProcurementPurchaseOrderService> logger;
    public ProcurementPurchaseOrderService(EduOSDbContext db, ICurrentUserService user,
        TimeProvider clock, ILogger<ProcurementPurchaseOrderService> logger)
    { this.db = db; this.user = user; this.clock = clock; this.logger = logger; }

    private bool Reader => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("InventoryManager") ||
         user.IsInRole("StoreKeeper") || user.IsInRole("Accountant"));
    private bool Maker => Reader && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        user.IsInRole("InventoryManager") || user.IsInRole("StoreKeeper"));
    private bool Checker => Reader && (user.IsTenantAdmin || user.IsInRole("Principal") ||
        user.IsInRole("InventoryManager"));
    private bool AllCampuses => user.IsTenantAdmin || user.IsInRole("Principal");
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Purchase order access denied.", 403);
    private static bool Version(byte[] current, string? supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) &&
            current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private IQueryable<ProcurementPurchaseOrder> Visible() =>
        db.ProcurementPurchaseOrders.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted &&
            db.Campuses.Any(c => c.TenantId == user.TenantId && c.Id == x.CampusId && !c.IsDeleted) &&
            (AllCampuses || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                a.UserId == user.UserId && a.CampusId == x.CampusId && a.IsActive && !a.IsDeleted)));
    private async Task<bool> CampusAllowed(long id, CancellationToken ct) =>
        id > 0 && await db.Campuses.AsNoTracking().AnyAsync(c =>
            c.TenantId == user.TenantId && c.Id == id && c.IsActive && !c.IsDeleted, ct) &&
        (AllCampuses || await db.UserCampusAccesses.AsNoTracking().AnyAsync(a =>
            a.TenantId == user.TenantId && a.CampusId == id && a.UserId == user.UserId &&
            a.IsActive && !a.IsDeleted, ct));

    private static ProcurementPurchaseOrderDto Map(ProcurementPurchaseOrder row,
        IEnumerable<ProcurementPurchaseOrderLine> lines)
    {
        var mapped = lines.OrderBy(x => x.LineNo).Select(x => new ProcurementPurchaseOrderLineDto
        {
            LineNo = x.LineNo, RequisitionLineId = x.RequisitionLineId,
            InventoryItemId = x.InventoryItemId, Quantity = x.Quantity, UnitPrice = x.UnitPrice
        }).ToList();
        return new ProcurementPurchaseOrderDto
        {
            Id = row.Id, ClientRequestId = row.ClientRequestId, OrderNumber = row.OrderNumber,
            CampusId = row.CampusId, RequisitionId = row.RequisitionId, SupplierId = row.SupplierId,
            CurrencyCode = row.CurrencyCode, StateCode = row.StateCode,
            ExpectedDeliveryDate = row.ExpectedDeliveryDate, SubmittedAt = row.SubmittedAt,
            DecidedAt = row.DecidedAt, DecidedByUserId = row.DecidedByUserId,
            DecisionReason = row.DecisionReason, CreatedBy = row.CreatedBy, CreatedAt = row.CreatedAt,
            RowVersion = Convert.ToBase64String(row.RowVersion),
            Lines = mapped, TotalAmount = mapped.Sum(x => x.Quantity * x.UnitPrice)
        };
    }
    private async Task<ProcurementPurchaseOrderDto> MapAsync(ProcurementPurchaseOrder row, CancellationToken ct)
    {
        var lines = await db.ProcurementPurchaseOrderLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.PurchaseOrderId == row.Id && !x.IsDeleted)
            .OrderBy(x => x.LineNo).ToListAsync(ct);
        return Map(row, lines);
    }
    public async Task<ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>> ListAsync(int page, int pageSize,
        long? campusId, string? state, string? search, CancellationToken ct = default)
    {
        if (!Reader) return Denied<PagedResult<ProcurementPurchaseOrderDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || state?.Length > 20 || search?.Length > 100)
            return ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>.ErrorResponse("Invalid filters.");
        var query = Visible();
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state);
        if (!string.IsNullOrWhiteSpace(search))
        { var term = search.Trim(); query = query.Where(x => x.OrderNumber.Contains(term)); }
        var count = await query.CountAsync(ct);
        var headers = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = headers.Select(x => x.Id).ToArray();
        var lines = await db.ProcurementPurchaseOrderLines.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && ids.Contains(x.PurchaseOrderId) && !x.IsDeleted).ToListAsync(ct);
        var byOrder = lines.GroupBy(x => x.PurchaseOrderId).ToDictionary(x => x.Key, x => x.AsEnumerable());
        return ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>.SuccessResponse(
            new PagedResult<ProcurementPurchaseOrderDto>
            {
                TotalCount = count, Page = page, PageSize = pageSize,
                Items = headers.Select(x => Map(x, byOrder.GetValueOrDefault(x.Id) ??
                    Enumerable.Empty<ProcurementPurchaseOrderLine>())).ToList()
            });
    }
    public async Task<ApiResponse<ProcurementPurchaseOrderDto>> GetAsync(long id, CancellationToken ct = default)
    {
        if (!Reader) return Denied<ProcurementPurchaseOrderDto>();
        var row = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Order not found.", 404)
            : ApiResponse<ProcurementPurchaseOrderDto>.SuccessResponse(await MapAsync(row, ct));
    }
    public async Task<ApiResponse<ProcurementPurchaseOrderDto>> CreateAsync(
        CreateProcurementPurchaseOrderRequest request, CancellationToken ct = default)
    {
        if (!Maker) return Denied<ProcurementPurchaseOrderDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.RequisitionId <= 0 ||
            request.SupplierId <= 0 || (request.ExpectedDeliveryDate.HasValue &&
            (request.ExpectedDeliveryDate.Value.TimeOfDay != TimeSpan.Zero ||
             request.ExpectedDeliveryDate.Value.Date < clock.GetUtcNow().UtcDateTime.Date)))
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Invalid purchase order request.");
        // Serializable isolation plus unique tenant/requisition and tenant/request indexes
        // prevents competing makers from issuing two orders for one approved requisition.
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var previous = await db.ProcurementPurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (previous != null)
            {
                if (!await CampusAllowed(previous.CampusId, ct) || previous.CreatedBy != user.UserId)
                    return Denied<ProcurementPurchaseOrderDto>();
                return previous.RequisitionId == request.RequisitionId &&
                    previous.SupplierId == request.SupplierId &&
                    previous.ExpectedDeliveryDate == request.ExpectedDeliveryDate
                    ? ApiResponse<ProcurementPurchaseOrderDto>.SuccessResponse(await MapAsync(previous, ct), "Existing request.")
                    : ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Request ID conflict.", 409);
            }
            var req = await db.ProcurementRequisitions.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.RequisitionId && !x.IsDeleted, ct);
            if (req == null || !await CampusAllowed(req.CampusId, ct))
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Requisition not found.", 404);
            if (req.StateCode != "Approved")
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Requisition must be approved.", 409);
            var supplier = await db.ProcurementSuppliers.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.SupplierId && !x.IsDeleted, ct);
            if (supplier == null || supplier.CampusId != req.CampusId || supplier.StateCode != "Approved")
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Approved campus supplier required.", 409);
            if (await db.ProcurementPurchaseOrders.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.RequisitionId == req.Id, ct))
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Requisition already has an order.", 409);
            var sourceLines = await db.ProcurementRequisitionLines.AsNoTracking()
                .Where(x => x.TenantId == user.TenantId && x.RequisitionId == req.Id && !x.IsDeleted)
                .OrderBy(x => x.LineNo).Take(101).ToListAsync(ct);
            if (sourceLines.Count is < 1 or > 100 || sourceLines.Any(x => x.Quantity <= 0 ||
                x.Quantity > 999999999m || x.EstimatedUnitPrice < 0 || x.EstimatedUnitPrice > 999999999m))
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Invalid approved lines.", 409);
            var itemIds = sourceLines.Select(x => x.InventoryItemId).ToArray();
            if (await db.InventoryItems.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                itemIds.Contains(x.Id) && x.IsActive && x.IsStockTracked && !x.IsDeleted, ct) != itemIds.Distinct().Count())
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Inactive requisition item.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var publicId = Guid.NewGuid();
            var order = new ProcurementPurchaseOrder
            {
                TenantId = user.TenantId, PublicId = publicId, ClientRequestId = request.ClientRequestId,
                OrderNumber = "PO-" + publicId.ToString("N").ToUpperInvariant(),
                CampusId = req.CampusId, RequisitionId = req.Id, SupplierId = supplier.Id,
                ExpectedDeliveryDate = request.ExpectedDeliveryDate,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.ProcurementPurchaseOrders.Add(order);
            await db.SaveChangesAsync(ct);
            foreach (var source in sourceLines)
                db.ProcurementPurchaseOrderLines.Add(new ProcurementPurchaseOrderLine
                {
                    TenantId = user.TenantId, PurchaseOrderId = order.Id, RequisitionLineId = source.Id,
                    LineNo = source.LineNo, InventoryItemId = source.InventoryItemId,
                    Quantity = source.Quantity, UnitPrice = source.EstimatedUnitPrice,
                    CreatedAt = now, CreatedBy = user.UserId
                });
            await db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            return ApiResponse<ProcurementPurchaseOrderDto>.SuccessResponse(await MapAsync(order, ct));
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Competing order; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            if (tx != null) await tx.RollbackAsync(ct);
            logger.LogWarning(ex, "Procurement PO uniqueness/integrity conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Order already exists or references changed.", 409);
        }
    }
    public async Task<ApiResponse<ProcurementPurchaseOrderDto>> TransitionAsync(long id, string action,
        ProcurementPurchaseOrderDecisionRequest request, CancellationToken ct = default)
    {
        if (!Reader) return Denied<ProcurementPurchaseOrderDto>();
        if (id <= 0 || request == null || action is not ("submit" or "approve" or "reject" or "revise" or "cancel") ||
            request.Reason?.Length > 500)
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Invalid decision.");
        var order = await db.ProcurementPurchaseOrders.FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (order == null || !await CampusAllowed(order.CampusId, ct))
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Order not found.", 404);
        var owner = order.CreatedBy == user.UserId;
        if ((action is "submit" or "revise" or "cancel" && (!Maker || !owner)) ||
            (action is "approve" or "reject" && (!Checker || owner)))
            return Denied<ProcurementPurchaseOrderDto>();
        if (!Version(order.RowVersion, request.RowVersion))
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Order changed; reload.", 409);
        var valid = action switch
        {
            "submit" => order.StateCode == "Draft",
            "approve" or "reject" => order.StateCode == "Submitted",
            "revise" => order.StateCode == "Rejected",
            "cancel" => order.StateCode is "Draft" or "Submitted" or "Rejected",
            _ => false
        };
        if (!valid) return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Invalid state transition.", 409);
        if (action is "reject" or "cancel" && string.IsNullOrWhiteSpace(request.Reason))
            return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Reason required.");
        if (action is "submit" or "approve")
        {
            var reqOk = await db.ProcurementRequisitions.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == order.RequisitionId &&
                x.CampusId == order.CampusId && x.StateCode == "Approved" && !x.IsDeleted, ct);
            var supplierOk = await db.ProcurementSuppliers.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == order.SupplierId &&
                x.CampusId == order.CampusId && x.StateCode == "Approved" && !x.IsDeleted, ct);
            if (!reqOk || !supplierOk)
                return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Source approval no longer valid.", 409);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        order.StateCode = action switch
        {
            "submit" => "Submitted", "approve" => "Approved", "reject" => "Rejected",
            "revise" => "Draft", _ => "Cancelled"
        };
        if (action == "submit")
        { order.SubmittedAt = now; order.DecidedAt = null; order.DecidedByUserId = null; order.DecisionReason = null; }
        if (action == "revise")
        { order.SubmittedAt = null; order.DecidedAt = null; order.DecidedByUserId = null; order.DecisionReason = null; }
        if (action is "approve" or "reject" or "cancel")
        { order.DecidedAt = now; order.DecidedByUserId = user.UserId; order.DecisionReason = request.Reason?.Trim(); }
        order.UpdatedAt = now; order.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<ProcurementPurchaseOrderDto>.SuccessResponse(await MapAsync(order, ct));
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<ProcurementPurchaseOrderDto>.ErrorResponse("Competing decision; reload.", 409); }
    }
}
