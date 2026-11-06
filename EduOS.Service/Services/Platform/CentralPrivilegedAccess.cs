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
        var activeMember = await db.TenantMemberships.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.UserId == user.UserId &&
            x.Status == MembershipStatus.Active && !x.IsDeleted, ct);
        if (!activeMember) return false;

        var roles = allowedNormalizedRoles.ToArray();
        return await db.UserRoles.AsNoTracking().AnyAsync(ur => ur.UserId == user.UserId &&
            db.Roles.IgnoreQueryFilters().AsNoTracking().Any(role =>
                role.Id == ur.RoleId && role.IsActive && !role.IsDeleted &&
                (role.TenantId == user.TenantId || (role.TenantId == null && role.IsSystemRole)) &&
                roles.Contains(role.NormalizedName!)), ct);
    }
}
