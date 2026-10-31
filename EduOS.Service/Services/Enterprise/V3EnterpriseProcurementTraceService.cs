using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

/// <summary>
/// Bounded, tenant-authorized, read-only requisition -> purchase-order evidence.
/// Procurement owns all mutations. No receipt, stock, capitalization or GL link
/// is inferred until the owning modules persist an explicit canonical reference.
/// </summary>
public sealed class V3EnterpriseProcurementTraceService(
    EduOSDbContext db, ICurrentUserService user)
{
    private sealed class Header
    {
        public long Id { get; set; }
        public Guid PublicId { get; set; }
        public string OrderNumber { get; set; } = "";
        public string StateCode { get; set; } = "";
        public long CampusId { get; set; }
        public long RequisitionId { get; set; }
        public long SupplierId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class Inspection
    {
        public string Status { get; set; } = "EvidenceConflict";
        public Guid RequisitionReference { get; set; }
        public Guid SupplierReference { get; set; }
        public decimal Total { get; set; }
        public V3EnterpriseProcurementTraceLineDto[] Lines { get; set; } = [];
    }

    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin &&
        user.TenantId > 0 && db.CurrentTenantId == user.TenantId;

    private IQueryable<Header> Visible() =>
        db.ProcurementPurchaseOrders.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted)
            .Select(x => new Header {
                Id = x.Id, PublicId = x.PublicId, OrderNumber = x.OrderNumber,
                StateCode = x.StateCode, CampusId = x.CampusId,
                RequisitionId = x.RequisitionId, SupplierId = x.SupplierId,
                CreatedAt = x.CreatedAt
            });

    public async Task<ApiResponse<V3EnterpriseProcurementTraceDto>> GetAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<V3EnterpriseProcurementTraceDto>.ErrorResponse("Access denied.", 403);
        if (reference == Guid.Empty)
            return ApiResponse<V3EnterpriseProcurementTraceDto>.ErrorResponse("Order reference required.", 400);
        var order = await Visible().SingleOrDefaultAsync(x => x.PublicId == reference, ct);
        if (order == null)
            return ApiResponse<V3EnterpriseProcurementTraceDto>.ErrorResponse("Order not found.", 404);
        var inspections = await InspectAsync([order], ct);
        if (inspections == null)
            return ApiResponse<V3EnterpriseProcurementTraceDto>.ErrorResponse(
                "Oversized historical commitment evidence requires investigation.", 409);
        var evidence = inspections[order.Id];
        if (evidence.Status == "EvidenceConflict")
            return ApiResponse<V3EnterpriseProcurementTraceDto>.ErrorResponse(
                "Purchase commitment source evidence requires reconciliation.", 409);
        return ApiResponse<V3EnterpriseProcurementTraceDto>.SuccessResponse(
            new V3EnterpriseProcurementTraceDto {
                OrderReference = order.PublicId, OrderNumber = order.OrderNumber,
                State = order.StateCode, CampusId = order.CampusId,
                RequisitionReference = evidence.RequisitionReference,
                SupplierReference = evidence.SupplierReference,
                TotalAmount = evidence.Total, LineCount = evidence.Lines.Length,
                EvidenceStatus = evidence.Status, Lines = evidence.Lines
            });
    }

    public async Task<ApiResponse<V3EnterpriseReconciliationRegisterDto>> RegisterAsync(
        DateOnly from, DateOnly to, long? campusId, int page, int size,
        CancellationToken ct = default)
    {
        if (!Allowed)
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse("Access denied.", 403);
        if (from > to || to.DayNumber - from.DayNumber > 366 ||
            page < 1 || page > 10000 || size < 1 || size > 50)
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse(
                "Invalid bounded report filters.", 400);
        if (campusId.HasValue && (campusId.Value <= 0 ||
            !await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && !x.IsDeleted &&
                x.Id == campusId.Value, ct)))
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse(
                "Campus not found.", 404);
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MaxValue);
        var query = Visible().Where(x => x.CreatedAt >= start && x.CreatedAt <= end);
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var inspections = await InspectAsync(orders, ct);
        if (inspections == null)
            return ApiResponse<V3EnterpriseReconciliationRegisterDto>.ErrorResponse(
                "Oversized historical commitment evidence requires investigation.", 409);
        var rows = orders.Select(x => new V3EnterpriseReconciliationRegisterRowDto {
            Reference = x.PublicId, Number = x.OrderNumber,
            Date = DateOnly.FromDateTime(x.CreatedAt), CampusId = x.CampusId,
            State = x.StateCode, RecordedValue = inspections[x.Id].Total,
            LineCount = inspections[x.Id].Lines.Length,
            EvidenceStatus = inspections[x.Id].Status
        }).ToArray();
        return ApiResponse<V3EnterpriseReconciliationRegisterDto>.SuccessResponse(
            new V3EnterpriseReconciliationRegisterDto {
                Kind = "procurement", From = from, To = to, CampusId = campusId,
                Page = page, PageSize = size, TotalCount = total, Items = rows
            });
    }

    // A single bounded set of SQL projections validates the entire current page.
    // Include soft-deleted historical rows to detect tombstones, never hide them.
    private async Task<Dictionary<long, Inspection>?> InspectAsync(
        IReadOnlyList<Header> orders, CancellationToken ct)
    {
        var output = orders.ToDictionary(x => x.Id, _ => new Inspection());
        if (orders.Count == 0) return output;
        if (orders.Count > 50) return null;
        var orderIds = orders.Select(x => x.Id).ToArray();
        var reqIds = orders.Select(x => x.RequisitionId).Distinct().ToArray();
        var supplierIds = orders.Select(x => x.SupplierId).Distinct().ToArray();
        var campusIds = orders.Select(x => x.CampusId).Distinct().ToArray();

        var orderLines = await db.ProcurementPurchaseOrderLines.IgnoreQueryFilters()
            .AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                orderIds.Contains(x.PurchaseOrderId))
            .OrderBy(x => x.PurchaseOrderId).ThenBy(x => x.LineNo).ThenBy(x => x.Id)
            .Select(x => new {
                x.PurchaseOrderId, x.RequisitionLineId, x.LineNo,
                x.InventoryItemId, x.Quantity, x.UnitPrice, x.IsDeleted
            }).Take(5001).ToListAsync(ct);
        var requestLines = await db.ProcurementRequisitionLines.IgnoreQueryFilters()
            .AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                reqIds.Contains(x.RequisitionId))
            .Select(x => new {
                x.Id, x.RequisitionId, x.LineNo, x.InventoryItemId,
                x.Quantity, x.EstimatedUnitPrice, x.IsDeleted
            }).Take(5001).ToListAsync(ct);
        if (orderLines.Count > 5000 || requestLines.Count > 5000) return null;

        var requisitions = await db.ProcurementRequisitions.IgnoreQueryFilters()
            .AsNoTracking().Where(x => x.TenantId == user.TenantId && reqIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.CampusId, x.StateCode, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var suppliers = await db.ProcurementSuppliers.IgnoreQueryFilters()
            .AsNoTracking().Where(x => x.TenantId == user.TenantId && supplierIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.CampusId, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var campuses = await db.Campuses.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && campusIds.Contains(x.Id))
            .Select(x => new { x.Id, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var itemIds = orderLines.Select(x => x.InventoryItemId).Distinct().ToArray();
        var items = await db.InventoryItems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && itemIds.Contains(x.Id))
            .Select(x => new { x.Id, x.IsDeleted })
            .ToDictionaryAsync(x => x.Id, ct);
        var poGroups = orderLines.GroupBy(x => x.PurchaseOrderId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var reqGroups = requestLines.GroupBy(x => x.RequisitionId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        foreach (var order in orders)
        {
            var inspected = output[order.Id];
            poGroups.TryGetValue(order.Id, out var lines);
            reqGroups.TryGetValue(order.RequisitionId, out var source);
            lines ??= [];
            source ??= [];
            inspected.Lines = lines.Select(x => new V3EnterpriseProcurementTraceLineDto {
                LineNo = x.LineNo, InventoryItemId = x.InventoryItemId,
                Quantity = x.Quantity, UnitPrice = x.UnitPrice
            }).ToArray();
            inspected.Total = inspected.Lines.Sum(x => x.Amount);
            if (requisitions.TryGetValue(order.RequisitionId, out var req))
                inspected.RequisitionReference = req.PublicId;
            if (suppliers.TryGetValue(order.SupplierId, out var supplier))
                inspected.SupplierReference = supplier.PublicId;

            var sourceById = source.GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());
            var invalid = !campuses.TryGetValue(order.CampusId, out var campus) ||
                campus.IsDeleted || req == null || req.IsDeleted ||
                req.CampusId != order.CampusId || req.StateCode != "Approved" ||
                supplier == null || supplier.IsDeleted || supplier.CampusId != order.CampusId ||
                lines.Length is < 1 or > 100 || source.Length != lines.Length ||
                source.Length is < 1 or > 100 ||
                lines.Any(x => x.IsDeleted || x.Quantity <= 0m || x.UnitPrice < 0m ||
                    !items.TryGetValue(x.InventoryItemId, out var item) || item.IsDeleted ||
                    !sourceById.TryGetValue(x.RequisitionLineId, out var original) ||
                    original.IsDeleted || original.RequisitionId != order.RequisitionId ||
                    original.InventoryItemId != x.InventoryItemId ||
                    original.LineNo != x.LineNo || x.Quantity > original.Quantity) ||
                lines.Select(x => x.RequisitionLineId).Distinct().Count() != lines.Length ||
                source.Any(x => x.IsDeleted || x.Quantity <= 0m || x.EstimatedUnitPrice < 0m) ||
                inspected.Total > source.Sum(x => x.Quantity * x.EstimatedUnitPrice);
            inspected.Status = invalid ? "EvidenceConflict" :
                order.StateCode == "Approved" ? "ApprovedCommitment" : "PendingApproval";
        }
        return output;
    }
}
