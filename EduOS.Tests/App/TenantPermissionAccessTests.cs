using EduOS.App.Authorization;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class TenantPermissionAccessTests
{
    [Fact]
    public async Task Role_permission_isolated_by_tenant_and_user_deny_takes_precedence()
    {
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        var authenticatedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "51"), new Claim(ClaimTypes.Role, "Teacher")
        }, "Test"));
        // Seed foreign-tenant fixtures as the platform, not as an institution user.
        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        accessor.HttpContext = http;
        await using var db = new EduOSDbContext(
            new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("perm-test-" + Guid.NewGuid().ToString("N")).Options, accessor);
        var right = new Permission { Id = 3, Code = "student_read", Name = "Read students", ModuleCode = "STUDENT" };
        db.Permissions.Add(right);
        db.Users.Add(new ApplicationUser { Id = 51, UserName = "teacher-51", IsActive = true });
        db.TenantMemberships.Add(new TenantMembership { Id = 1, TenantId = 101,
            UserId = 51, Status = MembershipStatus.Active });
        db.Roles.AddRange(
            new ApplicationRole { Id = 11, TenantId = 101, Name = "Teacher", NormalizedName = "TEACHER" },
            new ApplicationRole { Id = 12, TenantId = 102, Name = "Teacher", NormalizedName = "TEACHER" });
        db.UserRoles.AddRange(
            new IdentityUserRole<long> { UserId = 51, RoleId = 11 },
            new IdentityUserRole<long> { UserId = 51, RoleId = 12 });
        db.RolePermissions.Add(new RolePermission { RoleId = 12, PermissionId = 3, IsAllowed = true });
        await db.SaveChangesAsync();
        http.Items["TenantId"] = 101L;
        http.User = authenticatedPrincipal;

        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.TenantId).Returns(101);
        current.SetupGet(x => x.UserId).Returns(51);
        current.SetupGet(x => x.IsTenantAdmin).Returns(false);
        var handler = new PermissionAccessHandler(db, current.Object);
        var requirement = new PermissionAccessRequirement("STUDENT_READ");
        async Task<bool> Allowed()
        {
            var context = new AuthorizationHandlerContext(new[] { requirement }, http.User, null);
            await handler.HandleAsync(context);
            return context.HasSucceeded;
        }
        Assert.False(await Allowed()); // Tenant 102 cannot authorize Tenant 101

        db.RolePermissions.Add(new RolePermission { RoleId = 11, PermissionId = 3, IsAllowed = true });
        await db.SaveChangesAsync();
        Assert.True(await Allowed());

        // A direct handler invocation with a stale or swapped DB context must
        // not grant permissions based on claims from a different institution.
        http.Items["TenantId"] = 102L;
        Assert.False(await Allowed());
        http.Items["TenantId"] = 101L;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "99"), new Claim(ClaimTypes.Role, "Teacher")
        }, "Test"));
        Assert.False(await Allowed());
        http.User = authenticatedPrincipal;
        Assert.True(await Allowed());

        db.UserPermissions.Add(new UserPermission
        {
            UserId = 51, TenantId = 101, PermissionId = 3, IsAllowed = false
        });
        await db.SaveChangesAsync();
        Assert.False(await Allowed());
    }

    [Fact]
    public async Task Tenant_administrator_cannot_receive_platform_exclusive_audit_delete_permission()
    {
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "51"), new Claim(ClaimTypes.Role, "TenantAdmin")
        }, "Test"));
        accessor.HttpContext = http;
        await using var db = new EduOSDbContext(
            new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("reserved-permission-" + Guid.NewGuid().ToString("N")).Options, accessor);
        db.Permissions.Add(new Permission
        {
            Id = 55, Code = "auditlog.delete", Name = "Delete audit logs", ModuleCode = "AUDIT"
        });
        await db.SaveChangesAsync();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.TenantId).Returns(101);
        current.SetupGet(x => x.UserId).Returns(51);
        current.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var requirement = new PermissionAccessRequirement("AUDITLOG.DELETE");
        var context = new AuthorizationHandlerContext(new[] { requirement }, http.User, null);
        await new PermissionAccessHandler(db, current.Object).HandleAsync(context);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Live_permission_checks_reject_revocation_and_system_roles_but_preserve_owner_membership()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "51"), new Claim(ClaimTypes.Role, "TenantAdmin")
        }, "Test"));
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("perm-live-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        db.Users.Add(new ApplicationUser { Id = 51, UserName = "tenant-admin", IsActive = true });
        var membership = new TenantMembership { Id = 1, TenantId = 101, UserId = 51,
            Status = MembershipStatus.Active };
        db.TenantMemberships.Add(membership);
        var role = new ApplicationRole { Id = 11, TenantId = 101, Name = "TenantAdmin",
            NormalizedName = "TENANTADMIN", IsActive = true };
        db.Roles.Add(role);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = 51, RoleId = 11 });
        db.Permissions.Add(new Permission { Id = 3, Code = "student_read",
            Name = "Student read", ModuleCode = "STUDENT" });
        await db.SaveChangesAsync();
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(x => x.IsAuthenticated).Returns(true);
        actor.SetupGet(x => x.TenantId).Returns(101);
        actor.SetupGet(x => x.UserId).Returns(51);
        actor.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var handler = new PermissionAccessHandler(db, actor.Object);
        var requirement = new PermissionAccessRequirement("STUDENT_READ");
        async Task<bool> Allowed()
        {
            var context = new AuthorizationHandlerContext(new[] { requirement }, http.User, null);
            await handler.HandleAsync(context);
            return context.HasSucceeded;
        }

        Assert.True(await Allowed());
        role.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False(await Allowed());
        membership.IsOwner = true;
        await db.SaveChangesAsync();
        Assert.True(await Allowed());
        membership.IsOwner = false;
        await db.SaveChangesAsync();
        // Global roles may only be provisioned by platform administration.
        // Seed one through that authority, then restore the tenant actor before authorization.
        var tenantPrincipal = http.User;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "SuperAdmin")
        }, "Test"));
        try
        {
            db.Roles.Add(new ApplicationRole { Id = 12, TenantId = null,
                Name = "TenantAdmin", NormalizedName = "TENANTADMIN",
                IsActive = true, IsSystemRole = true });
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = 51, RoleId = 12 });
            await db.SaveChangesAsync();
        }
        finally
        {
            http.User = tenantPrincipal;
        }
        Assert.False(await Allowed());
        role.IsActive = true;
        membership.Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        Assert.False(await Allowed());
    }

    [Fact]
    public async Task Permission_policy_is_available_from_registered_provider_without_changing_module_policy()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions());
        var provider = new ModuleAuthorizationPolicyProvider(options);
        var policy = await provider.GetPolicyAsync("EduOSPermission:STUDENT_READ");
        Assert.NotNull(policy);
        Assert.Contains(policy!.Requirements, x => x is PermissionAccessRequirement);
        Assert.NotNull(await provider.GetPolicyAsync("EduOSModule:STUDENT"));
    }
}
