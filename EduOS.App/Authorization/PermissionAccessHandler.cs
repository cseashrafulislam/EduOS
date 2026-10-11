using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EduOS.App.Authorization;

/// <summary>
/// Permission checks always use the trusted tenant context. User-specific DENY
/// overrides role ALLOW. Non-annotated legacy endpoints retain existing policies.
/// </summary>
public sealed class PermissionAccessHandler(EduOSDbContext db, ICurrentUserService current)
    : AuthorizationHandler<PermissionAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        PermissionAccessRequirement requirement)
    {
        if (!current.IsAuthenticated || current.TenantId <= 0 || current.UserId <= 0) return;
        var tenant = current.TenantId;
        var user = current.UserId;
        if (current.IsTenantAdmin)
        {
            context.Succeed(requirement);
            return;
        }
        var code = requirement.PermissionCode;
        var now = DateTime.UtcNow;
        var permissionId = await db.Permissions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && x.Code == code)
            .Select(x => x.Id).FirstOrDefaultAsync();
        if (permissionId <= 0) return;
        var overridePermission = await db.UserPermissions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.UserId == user && x.TenantId == tenant &&
                x.PermissionId == permissionId &&
                (x.ExpiresAt == null || x.ExpiresAt > now))
            .Select(x => (bool?)x.IsAllowed).FirstOrDefaultAsync();
        if (overridePermission == false) return;
        if (overridePermission == true)
        {
            context.Succeed(requirement);
            return;
        }
        var activeRoles = context.User.FindAll(ClaimTypes.Role).Select(x => x.Value)
            .Where(x => x != "SuperAdmin").Distinct().ToArray();
        if (activeRoles.Length == 0) return;

        // TenantContextMiddleware already validated that these are exactly the
        // current institution's canonical roles, but confirm the role FK scope.
        var granted = await (from ur in db.UserRoles.AsNoTracking()
                             join role in db.Roles.IgnoreQueryFilters().AsNoTracking()
                                 on ur.RoleId equals role.Id
                             join rp in db.RolePermissions.AsNoTracking()
                                 on role.Id equals rp.RoleId
                             where ur.UserId == user && role.TenantId == tenant &&
                                 !role.IsDeleted && role.IsActive && !role.IsSystemRole &&
                                 activeRoles.Contains(role.Name!) &&
                                 !rp.IsDeleted && rp.IsAllowed && rp.PermissionId == permissionId
                             select rp.Id).AnyAsync();
        if (granted) context.Succeed(requirement);
    }
}
