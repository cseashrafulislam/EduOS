using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Platform;

/// <summary>
/// Live privileged-session check for direct service entry points. A trusted tenant
/// context and cookie role alone are insufficient after a membership or role revoke.
/// </summary>
internal static class CentralPrivilegedAccess
{
    public static async Task<bool> HasRoleAsync(EduOSDbContext db, ICurrentUserService user,
        IReadOnlyCollection<string> allowedNormalizedRoles, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId <= 0 || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId || allowedNormalizedRoles.Count == 0)
            return false;

        var activeUser = await db.Users.AsNoTracking().AnyAsync(x =>
            x.Id == user.UserId && x.IsActive && !x.IsDeleted, ct);
        if (!activeUser) return false;
        var member = await db.TenantMemberships.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.UserId == user.UserId &&
            x.Status == MembershipStatus.Active && !x.IsDeleted)
            .Select(x => new { x.IsOwner }).FirstOrDefaultAsync(ct);
        if (member == null) return false;

        var roles = allowedNormalizedRoles.ToArray();
        // The canonical TenantRoleResolver grants TenantAdmin to an active owner
        // independently of Identity role rows; do not lock owners out on revoke.
        if (member.IsOwner && roles.Contains("TENANTADMIN")) return true;

        // Legacy unscoped non-system roles may authorize only a user associated
        // with one institution overall, including suspended memberships.
        var membershipCount = await db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.UserId == user.UserId && !x.IsDeleted && x.Status != MembershipStatus.Left)
            .Select(x => x.TenantId).Distinct().Take(2).CountAsync(ct);
        var allowLegacyRole = membershipCount == 1;
        return await db.UserRoles.AsNoTracking().AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().AsNoTracking().Any(role =>
                role.Id == ur.RoleId && role.IsActive && !role.IsDeleted &&
                (role.TenantId == user.TenantId ||
                 (allowLegacyRole && role.TenantId == null && !role.IsSystemRole)) &&
                roles.Contains(role.NormalizedName!)), ct);
    }
}
