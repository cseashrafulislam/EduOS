using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Facilities;

public sealed partial class FacilityWorkflowService
{
    // Read from the stock ledger, never from a second editable work-order parts table.
    // Cancellation may proceed only after reversal of every issued stock movement.
    private async Task<bool> HasUnsettledPartsAsync(long workOrderId, bool includePosted, CancellationToken ct)
    {
        var parts = db.InventoryMovements.AsNoTracking().Where(m => m.TenantId == user.TenantId &&
            m.SourceType == "FacilityWorkOrder" && m.SourceId == workOrderId && !m.IsDeleted);
        if (await parts.AnyAsync(m => m.StateCode == "Draft" || m.StateCode == "Reserved", ct)) return true;
        return includePosted && await parts.AnyAsync(m => m.StateCode == "Posted" &&
            !db.InventoryMovements.Any(reversal => reversal.TenantId == user.TenantId &&
                reversal.ReversalOfMovementId == m.Id && reversal.StateCode == "Posted" && !reversal.IsDeleted), ct);
    }

    public async Task<ApiResponse<PagedResult<FacilityPartsIssueDto>>> PartsAsync(
        long facilityRequestId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!ValidTenant || !Reader) return Denied<PagedResult<FacilityPartsIssueDto>>();
        if (facilityRequestId <= 0 || page <= 0 || pageSize is < 1 or > 100)
            return ApiResponse<PagedResult<FacilityPartsIssueDto>>.ErrorResponse("Invalid request or page parameters.");
        if (!await VisibleRequests().AnyAsync(r => r.Id == facilityRequestId, ct))
            return ApiResponse<PagedResult<FacilityPartsIssueDto>>.ErrorResponse("Facility request not found.", 404);
        var orderId = await db.FacilityWorkOrders.AsNoTracking()
            .Where(w => w.TenantId == user.TenantId && w.FacilityServiceRequestId == facilityRequestId && !w.IsDeleted)
            .Select(w => (long?)w.Id).FirstOrDefaultAsync(ct);
        if (!orderId.HasValue)
            return ApiResponse<PagedResult<FacilityPartsIssueDto>>.SuccessResponse(new() { Page = page, PageSize = pageSize });

        var query = db.InventoryMovements.AsNoTracking().Where(m => m.TenantId == user.TenantId &&
            m.SourceType == "FacilityWorkOrder" && m.SourceId == orderId.Value && !m.IsDeleted);
        var count = await query.CountAsync(ct);
        var movements = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new { m.Id, m.PublicId, m.MovementNumber, m.StateCode, m.FromLocationId,
                m.CreatedAt, m.PostedAt }).ToListAsync(ct);
        var ids = movements.Select(m => m.Id).ToArray();
        var lines = await db.InventoryMovementLines.AsNoTracking()
            .Where(l => l.TenantId == user.TenantId && ids.Contains(l.InventoryMovementId) && !l.IsDeleted)
            .OrderBy(l => l.LineNo).Select(l => new { l.InventoryMovementId, l.InventoryItemId, l.Quantity,
                l.UnitCost, l.LotOrBatchNo }).ToListAsync(ct);
        var reversals = await db.InventoryMovements.AsNoTracking().Where(m =>
            m.TenantId == user.TenantId && m.ReversalOfMovementId.HasValue &&
            ids.Contains(m.ReversalOfMovementId.Value) && m.StateCode == "Posted" && !m.IsDeleted)
            .Select(m => new { m.ReversalOfMovementId, m.PublicId }).ToListAsync(ct);
        var journals = await db.Journals.AsNoTracking().Where(j => j.TenantId == user.TenantId &&
            j.SourceType == "InventoryMovement" && j.SourceId.HasValue &&
            ids.Contains(j.SourceId.Value) && !j.IsDeleted)
            .Select(j => new { j.Id, j.SourceId, j.State }).ToListAsync(ct);

        var rows = movements.Select(m =>
        {
            var reversal = reversals.FirstOrDefault(r => r.ReversalOfMovementId == m.Id);
            var journal = journals.Where(j => j.SourceId == m.Id)
                .OrderByDescending(j => j.State.ToString() == "Posted").ThenByDescending(j => j.Id).FirstOrDefault();
            var detail = lines.Where(l => l.InventoryMovementId == m.Id)
                .Select(l => new FacilityPartsIssueLineDto { InventoryItemId = l.InventoryItemId,
                    Quantity = l.Quantity, PostedUnitCost = l.UnitCost, LotOrBatchNo = l.LotOrBatchNo ?? "" }).ToArray();
            var cost = m.StateCode == "Posted" ? detail.Sum(l => l.Quantity * l.PostedUnitCost) : 0m;
            return new FacilityPartsIssueDto {
                Reference = m.PublicId, MovementNumber = m.MovementNumber,
                StateCode = reversal == null ? m.StateCode : "Reversed",
                FromLocationId = m.FromLocationId!.Value, CreatedAtUtc = m.CreatedAt,
                PostedAtUtc = m.PostedAt, ReversalReference = reversal?.PublicId,
                FinanceJournalId = journal?.Id,
                FinanceState = m.StateCode != "Posted" ? "NotPosted" : journal == null ? "Unlinked" : journal.State.ToString(),
                GrossCost = cost, NetCost = reversal == null ? cost : 0m, Lines = detail
            };
        }).ToList();
        return ApiResponse<PagedResult<FacilityPartsIssueDto>>.SuccessResponse(new() {
            Items = rows, TotalCount = count, Page = page, PageSize = pageSize
        });
    }
}
