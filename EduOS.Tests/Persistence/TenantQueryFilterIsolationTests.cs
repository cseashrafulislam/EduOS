using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;
using System.Security.Claims;

namespace EduOS.Tests.Persistence;

public sealed class TenantQueryFilterIsolationTests
{
    [Fact]
    public async Task Cached_model_uses_the_current_dbcontext_tenant_for_each_scope()
    {
        var name = "tenant-filter-" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name).Options;

        // Both contexts use the same EF model and database, but different authorized tenant scopes.
        await using (var first = Context(options, 101))
        {
            first.InventoryLocations.Add(new InventoryLocation
            {
                TenantId = 101, Code = "ONE", Name = "Tenant one store"
            });
            await first.SaveChangesAsync();
        }
        await using (var second = Context(options, 202))
        {
            second.InventoryLocations.Add(new InventoryLocation
            {
                TenantId = 202, Code = "TWO", Name = "Tenant two store"
            });
            await second.SaveChangesAsync();
            Assert.Equal(new[] { "TWO" }, await second.InventoryLocations.Select(x => x.Code).ToArrayAsync());
        }
        await using (var again = Context(options, 101))
        {
            Assert.Equal(new[] { "ONE" }, await again.InventoryLocations.Select(x => x.Code).ToArrayAsync());
            Assert.Equal(2, await again.InventoryLocations.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public async Task Subscription_filters_follow_request_context_even_after_model_cache_is_warmed()
    {
        var name = "subscription-filter-" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name).Options;
        await using (var first = Context(options, 101))
        {
            first.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = 101, SubscriptionPlanId = 1, State = SubscriptionState.PendingPayment,
                StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddDays(30)
            });
            await first.SaveChangesAsync();
        }
        await using (var second = Context(options, 202))
        {
            second.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = 202, SubscriptionPlanId = 1, State = SubscriptionState.PendingPayment,
                StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddDays(30)
            });
            await second.SaveChangesAsync();
            Assert.Equal(1, await second.TenantSubscriptions.CountAsync());
        }
        await using var again = Context(options, 101);
        Assert.Equal(1, await again.TenantSubscriptions.CountAsync());
    }

    [Fact]
    public async Task Authenticated_scopes_and_anonymous_system_scope_do_not_share_tenant_values()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("tenant-auth-" + Guid.NewGuid().ToString("N")).Options;
        var firstHttp = new DefaultHttpContext();
        firstHttp.Items["TenantId"] = 101L;
        firstHttp.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "InventoryManager")],
            "Test"));
        var firstAccessor = new HttpContextAccessor { HttpContext = firstHttp };
        await using var first = new EduOSDbContext(options, firstAccessor);
        first.InventoryLocations.Add(new InventoryLocation
        {
            TenantId = 101, Code = "OWNED", Name = "Owned"
        });
        await first.SaveChangesAsync();
        var foreign = new DefaultHttpContext();
        foreign.Items["TenantId"] = 202L;
        await using (var other = new EduOSDbContext(options,
            new HttpContextAccessor { HttpContext = foreign }))
        {
            other.InventoryLocations.Add(new InventoryLocation
            {
                TenantId = 202, Code = "FOREIGN", Name = "Foreign"
            });
            await other.SaveChangesAsync();
        }
        // The second simulated request ran sequentially in the same AsyncLocal flow.
        // Restore the original request before accessing its DbContext again.
        firstAccessor.HttpContext = firstHttp;
        Assert.Equal(101L, first.CurrentTenantId);
        Assert.False(await first.InventoryLocations.AnyAsync(x => x.Code == "FOREIGN"));
        Assert.Equal(2, await first.InventoryLocations.IgnoreQueryFilters().CountAsync());
    }

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
}
