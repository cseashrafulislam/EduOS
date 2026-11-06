using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CentralInventoryFinanceTenantScopeTests
{
    [Theory]
    [InlineData(101, 17, true, 200)]
    [InlineData(102, 17, true, 403)]
    [InlineData(101, 0, true, 403)]
    [InlineData(101, 17, false, 403)]
    public async Task Canonical_inventory_and_finance_services_require_matching_tenant_actor(
        long actorTenant, long userId, bool authenticated, int expected)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("central-scope-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var actor = new Actor(actorTenant, userId, authenticated);
        var inventory = new InventoryMovementService(db, actor, TimeProvider.System,
            NullLogger<InventoryMovementService>.Instance);
        var accounting = new AccountingPostingService(db, actor, TimeProvider.System);

        var stock = await inventory.GetMovementsAsync(null, null, 1, 20);
        var journals = await accounting.GetJournalsAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, 20);
        Assert.Equal(expected, stock.StatusCode);
        Assert.Equal(expected, journals.StatusCode);
    }

    private sealed class Actor(long tenant, long id, bool authenticated) : ICurrentUserService
    {
        public bool IsAuthenticated => authenticated;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
