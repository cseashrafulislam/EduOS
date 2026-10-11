using EduOS.App.Authorization;
using EduOS.Core.Entities.Auth;
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
        http.Items["TenantId"] = 101L;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "51"), new Claim(ClaimTypes.Role, "Teacher")
        }, "Test"));
        accessor.HttpContext = http;
        await using var db = new EduOSDbContext(
            new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("perm-test-" + Guid.NewGuid().ToString("N")).Options, accessor);
        var right = new Permission { Id = 3, Code = "STUDENT_READ", Name = "Read students", ModuleCode = "STUDENT" };
        db.Permissions.Add(right);
        db.Roles.AddRange(
            new ApplicationRole { Id = 11, TenantId = 101, Name = "Teacher", NormalizedName = "TEACHER" },
            new ApplicationRole { Id = 12, TenantId = 102, Name = "Teacher", NormalizedName = "TEACHER" });
        db.UserRoles.AddRange(
            new IdentityUserRole<long> { UserId = 51, RoleId = 11 },
            new IdentityUserRole<long> { UserId = 51, RoleId = 12 });
        db.RolePermissions.Add(new RolePermission { RoleId = 12, PermissionId = 3, IsAllowed = true });
        await db.SaveChangesAsync();

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
