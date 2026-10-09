using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InventoryLocationCampusAccessTests
{
    [Fact]
    public async Task Location_reads_respect_user_campus_grants_and_revocation()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("inventory-campus-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "Test"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var first = new Campus { TenantId = 101, Code = "A", Name = "Campus A" };
        var second = new Campus { TenantId = 101, Code = "B", Name = "Campus B" };
        db.Campuses.AddRange(first, second);
        await db.SaveChangesAsync();
        db.InventoryLocations.AddRange(
            new InventoryLocation { TenantId = 101, Code = "A1", Name = "Store A", CampusId = first.Id },
            new InventoryLocation { TenantId = 101, Code = "B1", Name = "Store B", CampusId = second.Id },
            new InventoryLocation { TenantId = 101, Code = "SHARED", Name = "Shared Store" });
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = first.Id, IsActive = true };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();

        var storeKeeper = new InventoryCatalogService(db, new User(101, "StoreKeeper"),
            TimeProvider.System, NullLogger<InventoryCatalogService>.Instance);
        var visible = await storeKeeper.GetLocationsAsync(null);
        visible.Data!.Select(x => x.Code).Should().BeEquivalentTo(["A1", "SHARED"]);
        (await storeKeeper.GetLocationsAsync(second.Id)).StatusCode.Should().Be(403);
        (await storeKeeper.GetLocationsAsync(first.Id)).Data!.Select(x => x.Code)
            .Should().BeEquivalentTo(["A1"]);

        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await storeKeeper.GetLocationsAsync(null)).Data!.Select(x => x.Code)
            .Should().BeEquivalentTo(["SHARED"]);
    }

    private sealed class User(long tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
