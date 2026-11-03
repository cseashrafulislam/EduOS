using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Inventory;

public sealed partial class FacilityPartsFinanceService
{
    public async Task<ApiResponse<PagedResult<FacilityPartsFinanceCandidateDto>>> ListAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Reader) return Error<PagedResult<FacilityPartsFinanceCandidateDto>>("Finance access denied.", 403);
        search = search?.Trim();
        if (page < 1 || pageSize is < 1 or > 50 || (search?.Length ?? 0) > 80 ||
            (long)page * pageSize > int.MaxValue)
            return Error<PagedResult<FacilityPartsFinanceCandidateDto>>("Invalid page or search.", 400);
        var requestFilter = long.TryParse(search, out var requestId) && requestId > 0;
        var referenceFilter = Guid.TryParse(search, out var reference);
        var query = from m in db.InventoryMovements.AsNoTracking()
            join w in db.FacilityWorkOrders.AsNoTracking() on m.SourceId equals (long?)w.Id
            join r in db.FacilityServiceRequests.AsNoTracking() on w.FacilityServiceRequestId equals r.Id
            where m.TenantId == user.TenantId && w.TenantId == user.TenantId && r.TenantId == user.TenantId &&
                m.SourceType == "FacilityWorkOrder" && m.MovementTypeCode == "Issue" &&
                m.StateCode == "Posted" && !m.IsDeleted && !w.IsDeleted && !r.IsDeleted &&
                db.Campuses.Any(c => c.TenantId == user.TenantId && c.Id == r.CampusId &&
                    !c.IsDeleted && c.IsActive) &&
                (AllCampuses || db.UserCampusAccesses.Any(a => a.TenantId == user.TenantId &&
                    a.UserId == user.UserId && a.CampusId == r.CampusId && a.IsActive && !a.IsDeleted)) &&
                (string.IsNullOrEmpty(search) || (requestFilter && r.Id == requestId) ||
                    (referenceFilter && m.PublicId == reference) || m.MovementNumber.StartsWith(search))
            select new { Movement = m, r.CampusId, RequestId = r.Id };
        var total = await query.CountAsync(ct);
        var movements = await query.OrderByDescending(x => x.Movement.PostedAt)
            .ThenByDescending(x => x.Movement.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Movement.Id, x.Movement.PublicId, x.Movement.MovementNumber,
                x.Movement.StateCode, x.CampusId, x.RequestId }).ToListAsync(ct);
        var ids = movements.Select(x => x.Id).ToArray();
        var costs = await db.InventoryMovementLines.AsNoTracking().Where(l => l.TenantId == user.TenantId &&
            ids.Contains(l.InventoryMovementId) && !l.IsDeleted)
            .GroupBy(l => l.InventoryMovementId)
            .Select(g => new { Id = g.Key, Cost = g.Sum(l => l.Quantity * l.UnitCost) }).ToListAsync(ct);
        var journals = await db.Journals.AsNoTracking().Where(j => j.TenantId == user.TenantId &&
            j.SourceType == "InventoryMovement" && j.SourceId.HasValue &&
            ids.Contains(j.SourceId.Value) && !j.IsDeleted && j.State != JournalState.Rejected &&
            j.State != JournalState.Cancelled).Select(j => new { j.Id, j.SourceId, j.State }).ToListAsync(ct);
        var journalIds = journals.Select(x => x.Id).ToArray();
        var reversals = await db.Journals.AsNoTracking().Where(j => j.TenantId == user.TenantId &&
            j.ReversalOfJournalId.HasValue && journalIds.Contains(j.ReversalOfJournalId.Value) &&
            j.SourceType == "FacilityPartsExpenseReversal" && j.State == JournalState.Posted && !j.IsDeleted)
            .Select(j => j.ReversalOfJournalId!.Value).ToListAsync(ct);
        var reversedStockIds = await db.InventoryMovements.AsNoTracking().Where(m =>
            m.TenantId == user.TenantId && m.ReversalOfMovementId.HasValue &&
            ids.Contains(m.ReversalOfMovementId.Value) && m.StateCode == "Posted" && !m.IsDeleted)
            .Select(m => m.ReversalOfMovementId!.Value).ToListAsync(ct);
        var result = movements.Select(m => {
            var journal = journals.FirstOrDefault(j => j.SourceId == m.Id);
            var cost = costs.FirstOrDefault(c => c.Id == m.Id)?.Cost ?? 0m;
            return new FacilityPartsFinanceCandidateDto {
                MovementReference = m.PublicId, MovementNumber = m.MovementNumber,
                FacilityRequestId = m.RequestId, CampusId = m.CampusId,
                StockCostBdt = decimal.Round(cost, 2, MidpointRounding.AwayFromZero),
                StockState = reversedStockIds.Contains(m.Id) ? "Reversed" : m.StateCode,
                FinanceState = journal == null ? "Unlinked" :
                    (reversals.Contains(journal.Id) != reversedStockIds.Contains(m.Id)) ? "OutOfSync" :
                    reversals.Contains(journal.Id) ? "Reversed" : journal.State.ToString()
            };
        }).ToArray();
        return ApiResponse<PagedResult<FacilityPartsFinanceCandidateDto>>.SuccessResponse(new() {
            Items = result, Page = page, PageSize = pageSize, TotalCount = total
        });
    }
}
