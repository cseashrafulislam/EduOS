using System.Security.Claims;
using EduOS.App.Middleware;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public sealed class TenantMembershipResolutionTests
{
    [Fact]
    public async Task Single_active_membership_resolves_without_legacy_user_tenant_property()
    {
        using var db = CreateContext();
        var tenantId = await SeedTenantAsync(db, "tenant-a");
        await SeedMembershipAsync(db, tenantId, 51, MembershipStatus.Active);
        var context = HttpContextFor(51);
        var invoked = false;
        var middleware = new TenantContextMiddleware(_ => { invoked = true; return Task.CompletedTask; },
            NullLogger<TenantContextMiddleware>.Instance);

        await middleware.InvokeAsync(context, CreateUserManager(51), db);

        Assert.True(invoked);
        Assert.Equal(tenantId, context.Items["TenantId"]);
    }

    [Fact]
    public async Task Signed_tenant_claim_is_rejected_when_active_membership_does_not_exist()
    {
        using var db = CreateContext();
        var allowedTenantId = await SeedTenantAsync(db, "tenant-a");
        var otherTenantId = await SeedTenantAsync(db, "tenant-b");
        await SeedMembershipAsync(db, allowedTenantId, 51, MembershipStatus.Active);
        var context = HttpContextFor(51, otherTenantId);
        var invoked = false;
        var middleware = new TenantContextMiddleware(_ => { invoked = true; return Task.CompletedTask; },
            NullLogger<TenantContextMiddleware>.Instance);

        await middleware.InvokeAsync(context, CreateUserManager(51), db);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(context.Items.ContainsKey("TenantId"));
    }

    [Fact]
    public async Task Multiple_memberships_without_a_selected_tenant_fail_closed()
    {
        using var db = CreateContext();
        var first = await SeedTenantAsync(db, "tenant-a");
        var second = await SeedTenantAsync(db, "tenant-b");
        await SeedMembershipAsync(db, first, 51, MembershipStatus.Active);
        await SeedMembershipAsync(db, second, 51, MembershipStatus.Active);
        var context = HttpContextFor(51);
        var invoked = false;
        var middleware = new TenantContextMiddleware(_ => { invoked = true; return Task.CompletedTask; },
            NullLogger<TenantContextMiddleware>.Instance);

        await middleware.InvokeAsync(context, CreateUserManager(51), db);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(context.Items.ContainsKey("TenantId"));
    }

    [Fact]
    public async Task Suspended_membership_is_not_authorized()
    {
        using var db = CreateContext();
        var tenantId = await SeedTenantAsync(db, "tenant-a");
        await SeedMembershipAsync(db, tenantId, 51, MembershipStatus.Suspended);
        var context = HttpContextFor(51, tenantId);
        var invoked = false;
        var middleware = new TenantContextMiddleware(_ => { invoked = true; return Task.CompletedTask; },
            NullLogger<TenantContextMiddleware>.Instance);

        await middleware.InvokeAsync(context, CreateUserManager(51), db);

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    private static EduOSDbContext CreateContext() => new(
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"membership-{Guid.NewGuid():N}").Options);

    private static async Task<long> SeedTenantAsync(EduOSDbContext db, string code)
    {
        var tenant = new Tenant { Name = code, Code = code, Email = code + "@example.test", IsActive = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task SeedMembershipAsync(EduOSDbContext db, long tenantId, long userId, MembershipStatus status)
    {
        using (db.BeginSystemTenantScope(tenantId))
        {
            db.TenantMemberships.Add(new TenantMembership { TenantId = tenantId, UserId = userId, Status = status });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
    }

    private static DefaultHttpContext HttpContextFor(long userId, long? selectedTenantId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, "Teacher")
        };
        if (selectedTenantId.HasValue)
            claims.Add(new Claim("TenantId", selectedTenantId.Value.ToString()));
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthentication")),
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    private static UserManager<ApplicationUser> CreateUserManager(long userId)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var manager = new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!,
            null!, null!, null!, null!);
        var user = new ApplicationUser { Id = userId, UserName = "teacher", IsActive = true };
        manager.Setup(x => x.FindByIdAsync(userId.ToString())).ReturnsAsync(user);
        manager.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Teacher" });
        return manager.Object;
    }
}
