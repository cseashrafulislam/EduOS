using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

/// <summary>
/// Current approved supplier commitments and delivery-date aging only.
/// Not an outstanding-to-receive, returned quantity, stock or payable ledger.
/// </summary>
public sealed partial class ProcurementPurchaseOrderService
{
    public async Task<ApiResponse<PagedResult<ProcurementPurchaseOrderCommitmentDto>>> CommitmentsAsync(
        int page, int pageSize, long? campusId, long? supplierId, DateTime? agingDate,
        bool overdueOnly, string? search, CancellationToken ct = default)
    {
        if (!Reader) return Denied<PagedResult<ProcurementPurchaseOrderCommitmentDto>>();
        if (page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || supplierId is <= 0 || search?.Length > 100 ||
            (agingDate.HasValue && agingDate.Value.TimeOfDay != TimeSpan.Zero))
            return ApiResponse<PagedResult<ProcurementPurchaseOrderCommitmentDto>>
                .ErrorResponse("Invalid commitment filters.");

        // Aging date is NOT a historical as-of snapshot; only current approved POs are shown.
        var asOf = agingDate?.Date ?? clock.GetUtcNow().UtcDateTime.Date;
        var query = Visible().Where(x => x.StateCode == "Approved");
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId.Value);
        if (overdueOnly)
            query = query.Where(x => x.ExpectedDeliveryDate.HasValue &&
                x.ExpectedDeliveryDate.Value < asOf);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.OrderNumber.Contains(term) ||
                db.ProcurementSuppliers.Any(s => s.TenantId == user.TenantId &&
                    s.Id == x.SupplierId && s.CampusId == x.CampusId &&
                    !s.IsDeleted && s.Name.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var headers = await query.OrderBy(x => x.ExpectedDeliveryDate == null)
            .ThenBy(x => x.ExpectedDeliveryDate).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.OrderNumber, x.CampusId, x.SupplierId,
                x.CurrencyCode, x.ExpectedDeliveryDate, x.DecidedAt }).ToListAsync(ct);
        var ids = headers.Select(x => x.Id).ToArray();
        var supplierIds = headers.Select(x => x.SupplierId).Distinct().ToArray();
        var campusIds = headers.Select(x => x.CampusId).Distinct().ToArray();
        var amounts = await db.ProcurementPurchaseOrderLines.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && ids.Contains(x.PurchaseOrderId) &&
                !x.IsDeleted)
            .GroupBy(x => x.PurchaseOrderId)
            .Select(g => new { OrderId = g.Key, LineCount = g.Count(),
                Amount = g.Sum(x => x.Quantity * x.UnitPrice) }).ToListAsync(ct);
        var amountById = amounts.ToDictionary(x => x.OrderId);
        // Supplier display data must match the authorized PO's campus, not merely its tenant.
        var supplierRows = await db.ProcurementSuppliers.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && supplierIds.Contains(x.Id) &&
                campusIds.Contains(x.CampusId) && !x.IsDeleted)
            .Select(x => new { x.Id, x.CampusId, x.Name }).ToListAsync(ct);
        var supplierNames = supplierRows.ToDictionary(x => (x.Id, x.CampusId), x => x.Name);
        var items = headers.Select(x =>
        {
            var due = x.ExpectedDeliveryDate?.Date;
            var overdue = due.HasValue && due.Value < asOf;
            amountById.TryGetValue(x.Id, out var sum);
            return new ProcurementPurchaseOrderCommitmentDto
            {
                Id = x.Id, OrderNumber = x.OrderNumber, CampusId = x.CampusId,
                SupplierId = x.SupplierId,
                SupplierName = supplierNames.GetValueOrDefault((x.SupplierId, x.CampusId)) ?? "",
                CurrencyCode = x.CurrencyCode, ExpectedDeliveryDate = x.ExpectedDeliveryDate,
                ApprovedAt = x.DecidedAt, AgingDate = asOf,
                DaysOverdue = overdue ? (asOf - due!.Value).Days : 0,
                DeliverySchedule = !due.HasValue ? "Unscheduled" :
                    overdue ? "Overdue" : due.Value == asOf ? "DueToday" : "Upcoming",
                LineCount = sum?.LineCount ?? 0, ApprovedAmount = sum?.Amount ?? 0m
            };
        }).ToList();
        return ApiResponse<PagedResult<ProcurementPurchaseOrderCommitmentDto>>.SuccessResponse(
            new PagedResult<ProcurementPurchaseOrderCommitmentDto>
            { TotalCount = total, Page = page, PageSize = pageSize, Items = items });
    }
}
