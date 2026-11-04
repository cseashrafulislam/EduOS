using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Enterprise;

/// <summary>Read-only, bounded proof that a facility stock issue still belongs
/// to an actual same-tenant work order, approved request, space and stock campus.
/// This is source verification, not a second inventory or Finance ledger.</summary>
public static class V3EnterpriseFacilityEvidenceValidator
{
    public sealed record Movement(long Id, long? SourceId, long? FromLocationId,
        long? ToLocationId, string StateCode, string MovementTypeCode,
        long? CreatedBy, long? PostedByUserId);

    public sealed class Result
    {
        public HashSet<long> Invalid { get; } = [];
        public HashSet<long> Verified { get; } = [];
    }

    public static async Task<Result> InspectAsync(EduOSDbContext db, long tenantId,
        IReadOnlyList<Movement> movements, CancellationToken ct = default)
    {
        var result = new Result();
        if (movements.Count == 0) return result;
        if (movements.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(movements),
                "Maximum 50 facility movement references per inspection.");
        var workOrderIds = movements.Where(x => x.SourceId is > 0)
            .Select(x => x.SourceId!.Value).Distinct().ToArray();
        var workOrders = await db.FacilityWorkOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.TenantId == tenantId && workOrderIds.Contains(w.Id))
            .Select(w => new { w.Id, w.FacilityServiceRequestId, w.IsDeleted })
            .ToDictionaryAsync(w => w.Id, ct);
        var requestIds = workOrders.Values.Select(w => w.FacilityServiceRequestId).Distinct().ToArray();
        var requests = await db.FacilityServiceRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == tenantId && requestIds.Contains(r.Id))
            .Select(r => new { r.Id, r.CampusId, r.FacilitySpaceId, r.RequesterUserId,
                r.ApprovedByUserId, r.ApprovedAtUtc, r.IsDeleted })
            .ToDictionaryAsync(r => r.Id, ct);
        var spaceIds = requests.Values.Select(r => r.FacilitySpaceId).Distinct().ToArray();
        var spaces = await db.FacilitySpaces.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenantId && spaceIds.Contains(s.Id))
            .Select(s => new { s.Id, s.CampusId, s.IsDeleted })
            .ToDictionaryAsync(s => s.Id, ct);
        var locationIds = movements.Where(m => m.FromLocationId is > 0)
            .Select(m => m.FromLocationId!.Value).Distinct().ToArray();
        var locations = await db.InventoryLocations.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == tenantId && locationIds.Contains(l.Id))
            .Select(l => new { l.Id, l.CampusId, l.IsDeleted })
            .ToDictionaryAsync(l => l.Id, ct);

        foreach (var movement in movements)
        {
            if (movement.SourceId is not > 0 || movement.FromLocationId is not > 0 ||
                !workOrders.TryGetValue(movement.SourceId.Value, out var workOrder) ||
                !requests.TryGetValue(workOrder.FacilityServiceRequestId, out var request) ||
                !spaces.TryGetValue(request.FacilitySpaceId, out var space) ||
                !locations.TryGetValue(movement.FromLocationId.Value, out var location) ||
                workOrder.IsDeleted || request.IsDeleted || space.IsDeleted || location.IsDeleted ||
                request.CampusId != space.CampusId ||
                (location.CampusId.HasValue && location.CampusId.Value != request.CampusId) ||
                request.ApprovedByUserId is null or <= 0 || !request.ApprovedAtUtc.HasValue ||
                request.ApprovedByUserId == request.RequesterUserId ||
                movement.MovementTypeCode != "Issue" || movement.ToLocationId.HasValue ||
                (movement.StateCode == "Posted" && (movement.CreatedBy is null or <= 0 ||
                    movement.PostedByUserId is null or <= 0 ||
                    movement.CreatedBy == movement.PostedByUserId)))
                result.Invalid.Add(movement.Id);
            else
                result.Verified.Add(movement.Id);
        }
        return result;
    }
}
