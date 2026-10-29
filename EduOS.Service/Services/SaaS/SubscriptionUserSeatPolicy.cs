using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.SaaS;

/// <summary>
/// Authoritative tenant seat check for subscription-limited institution users.
/// Must be called inside the same Serializable transaction that creates a
/// membership; a preflight-only check cannot prevent concurrent overbooking.
/// Suspended memberships reserve their seat until they leave the institution.
/// </summary>
public static class SubscriptionUserSeatPolicy
{
    public static async Task<bool> CanAddMemberAsync(EduOSDbContext db, long tenantId,
        DateTime utcNow, CancellationToken ct = default)
    {
        if (tenantId <= 0) return false;

        var planId = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted &&
                x.StartsAt <= utcNow && x.EndsAt > utcNow &&
                (x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active ||
                 x.State == SubscriptionState.Grace))
            .OrderByDescending(x => x.StartsAt).ThenByDescending(x => x.Id)
            .Select(x => (long?)x.SubscriptionPlanId).FirstOrDefaultAsync(ct);

        // No effective subscription is handled by the existing subscription
        // access gate. Do not infer an undocumented user limit for that state.
        if (!planId.HasValue) return true;
        var maxUsers = await db.SubscriptionPlans.AsNoTracking()
            .Where(x => x.Id == planId.Value && !x.IsDeleted)
            .Select(x => (int?)x.MaxUsers).FirstOrDefaultAsync(ct);
        if (maxUsers is null or <= 0) return true;

        var reservedSeats = await db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && !x.IsDeleted &&
                x.Status != MembershipStatus.Left, ct);
        return reservedSeats < maxUsers.Value;
    }
}
