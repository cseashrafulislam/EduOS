using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Auth;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class TenantUserAdministrationTests
{
    [Fact]
    public async Task Roles_only_list_matching_institution_and_reserved_name_is_rejected()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        db.Roles.AddRange(
            new ApplicationRole { TenantId = 100, Name = "Teacher", NormalizedName = "TEACHER" },
            new ApplicationRole { TenantId = 200, Name = "Principal", NormalizedName = "PRINCIPAL" });
        await db.SaveChangesAsync();
        var service = Service(db, access.User, access.Email);
        var roles = await service.GetRolesAsync();
        Assert.True(roles.Success);
        Assert.Single(roles.Data!);
        Assert.Equal("Teacher", roles.Data![0].Name);
        var forbidden = await service.CreateRoleAsync(new TenantRoleCreateRequestDto { Name = "SuperAdmin" });
        Assert.False(forbidden.Success);
        Assert.Equal(400, forbidden.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_assign_permissions_to_another_tenants_role()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        db.Roles.Add(new ApplicationRole
        {
            Id = 222, TenantId = 200, Name = "Accountant",
            NormalizedName = "ACCOUNTANT", IsActive = true
        });
        await db.SaveChangesAsync();
        var service = Service(db, access.User, access.Email);
        var result = await service.SetRolePermissionsAsync(222,
            new TenantRolePermissionsRequestDto { PermissionIds = Array.Empty<long>(), ConcurrencyStamp = "test-stamp" });
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
        Assert.Empty(db.RolePermissions);
    }

    [Fact]
    public async Task Non_tenant_admin_is_denied_role_creation_and_membership_listing()
    {
        var access = ContextFor(100, isAdmin: false);
        await using var db = access.Db;
        var service = Service(db, access.User, access.Email);
        Assert.Equal(403, (await service.CreateRoleAsync(
            new TenantRoleCreateRequestDto { Name = "Teacher" })).StatusCode);
        Assert.Equal(403, (await service.GetMembersAsync(1, 20, null)).StatusCode);
        Assert.Empty(db.Roles);
    }

    [Fact]
    public async Task Cross_tenant_role_cannot_be_assigned_to_member_even_with_valid_token()
    {
        var access = ContextFor(100, isAdmin: true);
        await using var db = access.Db;
        db.Roles.Add(new ApplicationRole
        {
            Id = 222, TenantId = 200, Name = "Accountant",
            NormalizedName = "ACCOUNTANT", IsActive = true
        });
        await db.SaveChangesAsync();
        var service = Service(db, access.User, access.Email);
        var result = await service.SetMemberRolesAsync(44,
            new TenantMemberRolesRequestDto
            {
                RowVersion = Convert.ToBase64String(new byte[8]), RoleIds = new[] { 222L }
            });
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Empty(db.UserRoles);
    }

    [Theory]
    [InlineData(nameof(TenantUsersController.CreateRole))]
    [InlineData(nameof(TenantUsersController.SetRoles))]
    [InlineData(nameof(TenantUsersController.SetStatus))]
    [InlineData(nameof(TenantUsersController.SetPermissions))]
    [InlineData(nameof(TenantUsersController.SendInvitation))]
    public void Tenant_user_management_mutations_are_protected(string methodName)
    {
        var controller = typeof(TenantUsersController);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute), true));
        var action = controller.GetMethod(methodName);
        Assert.NotNull(action);
        Assert.Empty(action!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Invitation_acceptance_is_public_but_individually_rate_limited()
    {
        var action = typeof(TenantUsersController).GetMethod(nameof(TenantUsersController.AcceptInvitation))!;
        Assert.NotEmpty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), true));
    }

    private static (EduOSDbContext Db, Mock<ICurrentUserService> User, Mock<IEmailService> Email)
        ContextFor(long tenantId, bool isAdmin)
    {
        var access = new HttpContextAccessor();
        var db = new EduOSDbContext(
            new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("tenant-users-" + Guid.NewGuid().ToString("N")).Options, access);
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Role, isAdmin ? "TenantAdmin" : "Teacher")
        }, "Test"));
        http.Items["TenantId"] = tenantId;
        access.HttpContext = http;
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.IsTenantAdmin).Returns(isAdmin);
        current.SetupGet(x => x.TenantId).Returns(tenantId);
        current.SetupGet(x => x.UserId).Returns(42);
        return (db, current, new Mock<IEmailService>());
    }

    private static TenantUserAdministrationService Service(
        EduOSDbContext db, Mock<ICurrentUserService> user, Mock<IEmailService> email)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var identity = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        return new TenantUserAdministrationService(db, user.Object, identity.Object, email.Object,
            Options.Create(new TenantPortalSettings { BaseDomain = "portal.example.test" }));
    }
}
