using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.App.Security;

/// <summary>
/// Identity's user-role join is global; only tenant-owned roles, or unambiguous
/// unscoped global Identity roles for single-institution users, can authorize tenant work.
/// An owner membership independently grants TenantAdmin for that institution.
/// </summary>
public static class TenantRoleResolver
{
    public static Task<bool> IsPlatformAdminAsync(EduOSDbContext db, long userId) =>
        (from assignment in db.UserRoles.AsNoTracking()
         join role in db.Roles.IgnoreQueryFilters().AsNoTracking() on assignment.RoleId equals role.Id
         where assignment.UserId == userId && role.TenantId == null && role.IsSystemRole &&
               role.IsActive && !role.IsDeleted && role.NormalizedName == "SUPERADMIN"
         select role.Id).AnyAsync();

    public static async Task<IReadOnlyList<string>> GetTenantRolesAsync(
        EduOSDbContext db, long userId, long tenantId, bool isOwner, int institutionMembershipCount)
    {
        if (tenantId <= 0) return Array.Empty<string>();
        var assigned = await (from assignment in db.UserRoles.AsNoTracking()
                              join role in db.Roles.IgnoreQueryFilters().AsNoTracking()
                                  on assignment.RoleId equals role.Id
                              where assignment.UserId == userId && role.IsActive && !role.IsDeleted &&
                                    role.Name != null && role.NormalizedName != "SUPERADMIN" &&
                                    (role.TenantId == tenantId ||
                                     (institutionMembershipCount == 1 && role.TenantId == null && !role.IsSystemRole))
                              select role.Name!).Distinct().ToListAsync();
        var roles = new HashSet<string>(assigned, StringComparer.OrdinalIgnoreCase);
        if (isOwner) roles.Add("TenantAdmin");
        if (roles.Count == 0) roles.Add("User");
        return roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
