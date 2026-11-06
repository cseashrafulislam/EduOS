using EduOS.Core.Entities.Auth;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;

namespace EduOS.Tests.Services;

internal static class CentralPrivilegedTestIdentity
{
    public static void Seed(EduOSDbContext db, long tenantId, long userId)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId, UserName = "central-admin-" + userId,
            IsActive = true
        });
        db.TenantMemberships.Add(new TenantMembership
        {
            Id = 9001, TenantId = tenantId, UserId = userId,
            Status = MembershipStatus.Active
        });
        db.Roles.Add(new ApplicationRole
        {
            Id = 9001, TenantId = tenantId,
            Name = "TenantAdmin", NormalizedName = "TENANTADMIN", IsActive = true
        });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = userId, RoleId = 9001 });
        db.SaveChanges();
    }
}
