using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using ApiTenantUsersController = EduOS.App.Controllers.Api.TenantUsersController;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

public sealed class TenantMemberPermissionOverrideTests
{
    [Fact]
    public void Permission_exception_workspace_requires_tenant_admin()
    {
        var controller = typeof(TenantPermissionOverridesController);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        var action = typeof(ApiTenantUsersController).GetMethod(nameof(ApiTenantUsersController.SetMemberPermissionOverride));
        Assert.NotNull(action);
        Assert.NotEmpty(typeof(ApiTenantUsersController).GetCustomAttributes(
            typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Empty(action!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public async Task Non_admin_cannot_list_or_change_permission_exceptions()
    {
        var (db, current, email) = Setup(100, false);
        await using (db)
        {
            var service = Service(db, current, email);
            Assert.Equal(403, (await service.GetMemberPermissionOverridesAsync(1)).StatusCode);
            Assert.Equal(403, (await service.SetMemberPermissionOverrideAsync(1, 1,
                new SetTenantMemberPermissionOverrideRequestDto())).StatusCode);
            Assert.Empty(db.UserPermissions);
        }
    }

    [Fact]
    public async Task Tenant_admin_cannot_read_foreign_members_permission_exceptions()
    {
        var (db, current, email) = Setup(100, true);
        await using (db)
        {
            Assert.Equal(404, (await Service(db, current, email)
                .GetMemberPermissionOverridesAsync(999)).StatusCode);
        }
    }

    [Theory]
    [InlineData("Allow")]
    [InlineData("Deny")]
    [InlineData("Inherit")]
    public async Task Missing_member_version_never_writes_override(string mode)
    {
        var (db, current, email) = Setup(100, true);
        await using (db)
        {
            var result = await Service(db, current, email).SetMemberPermissionOverrideAsync(1, 1,
                new SetTenantMemberPermissionOverrideRequestDto { Mode = mode });
            Assert.Equal(400, result.StatusCode);
            Assert.Empty(db.UserPermissions);
        }
    }

    private static (EduOSDbContext Db, Mock<ICurrentUserService> User, Mock<IEmailService> Email)
        Setup(long tenantId, bool isAdmin)
    {
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Role, isAdmin ? "TenantAdmin" : "Teacher")
        }, "Test"));
        accessor.HttpContext = http;
        var db = new EduOSDbContext(
            new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("permission-exceptions-" + Guid.NewGuid().ToString("N")).Options,
            accessor);
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
