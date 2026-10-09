using EduOS.Core.DTOs.Inventory;
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


    [Fact]
    public async Task Location_writes_require_current_campus_grant_for_source_and_destination()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("inventory-campus-write-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "InventoryManager")], "Test"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var first = new Campus { TenantId = 101, Code = "A", Name = "Campus A" };
        var second = new Campus { TenantId = 101, Code = "B", Name = "Campus B" };
        db.Campuses.AddRange(first, second);
        await db.SaveChangesAsync();
        var owned = new InventoryLocation { TenantId = 101, CampusId = first.Id, Code = "A1", Name = "Store A", RowVersion = [1] };
        var foreign = new InventoryLocation { TenantId = 101, CampusId = second.Id, Code = "B1", Name = "Store B", RowVersion = [1] };
        var shared = new InventoryLocation { TenantId = 101, Code = "SHARED", Name = "Shared Store", RowVersion = [1] };
        db.InventoryLocations.AddRange(owned, foreign, shared);
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = first.Id, IsActive = true };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        var service = new InventoryCatalogService(db, new User(101, "InventoryManager"),
            TimeProvider.System, NullLogger<InventoryCatalogService>.Instance);
        var version = Convert.ToBase64String(new byte[] { 1 });
        SaveInventoryLocationRequestDto Request(long? campus, string code, string name, string? rowVersion = null) =>
            new() { CampusId = campus, Code = code, Name = name, RowVersion = rowVersion };

        (await service.SaveLocationAsync(null, Request(second.Id, "B2", "No grant"))).StatusCode.Should().Be(403);
        (await service.SaveLocationAsync(null, Request(null, "SH2", "Shared"))).StatusCode.Should().Be(403);
        (await service.SaveLocationAsync(foreign.Id, Request(second.Id, "B1", "Modified", version))).StatusCode.Should().Be(403);
        (await service.SaveLocationAsync(shared.Id, Request(null, "SHARED", "Modified", version))).StatusCode.Should().Be(403);
        (await service.SaveLocationAsync(owned.Id, Request(second.Id, "A1", "Moved", version))).StatusCode.Should().Be(403);
        (await service.SaveLocationAsync(null, Request(first.Id, "B1", "Store B"))).StatusCode.Should().Be(403);
        (await service.SaveLocationAsync(null, Request(first.Id, "A2", "Store A2"))).StatusCode.Should().Be(201);
        owned.CampusId.Should().Be(first.Id);
        foreign.Name.Should().Be("Store B");
        shared.Name.Should().Be("Shared Store");
        (await db.InventoryLocations.CountAsync()).Should().Be(4);

        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await service.SaveLocationAsync(null, Request(first.Id, "A3", "Revoked"))).StatusCode.Should().Be(403);
        (await service.SaveLocationAsync(owned.Id, Request(first.Id, "A1", "Modified", version))).StatusCode.Should().Be(403);
        (await new InventoryCatalogService(db, new User(101, "TenantAdmin"),
            TimeProvider.System, NullLogger<InventoryCatalogService>.Instance)
            .SaveLocationAsync(null, Request(null, "SH2", "Shared"))).StatusCode.Should().Be(201);
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
