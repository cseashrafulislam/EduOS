using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Facilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FacilitySpaceServiceTests
{
    [Fact]
    public async Task Campus_grant_and_tenant_isolation_apply_to_space_reads_and_writes()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("facilities-spaces-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "FacilitiesManager")], "Test"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var a = new Campus { TenantId = 101, Code = "A", Name = "A" };
        var b = new Campus { TenantId = 101, Code = "B", Name = "B" };
        db.Campuses.AddRange(a, b);
        await db.SaveChangesAsync();
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = a.Id, IsActive = true };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        var service = new FacilitySpaceService(db, new Principal(101, "FacilitiesManager"),
            TimeProvider.System, NullLogger<FacilitySpaceService>.Instance);
        var create = await service.SaveAsync(null, new SaveFacilitySpaceRequestDto
            { CampusId = a.Id, Code = "LAB-1", Name = "Workshop" });
        create.StatusCode.Should().Be(201);
        (await service.SaveAsync(null, new SaveFacilitySpaceRequestDto
            { CampusId = b.Id, Code = "LAB-2", Name = "Blocked" })).StatusCode.Should().Be(403);
        (await service.ListAsync(b.Id, null, 1, 25)).StatusCode.Should().Be(403);
        (await service.ListAsync(null, null, 1, 25)).Data!.Items.Select(x => x.Code)
            .Should().Equal("LAB-1");
        (await service.ListAsync(null, null, 1, 101)).StatusCode.Should().Be(400);
        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await service.ListAsync(null, null, 1, 25)).Data!.Items.Should().BeEmpty();
        (await service.SaveAsync(null, new SaveFacilitySpaceRequestDto
            { CampusId = a.Id, Code = "LAB-3", Name = "Revoked" })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Reused_code_requires_identical_payload_and_stale_version_is_rejected()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("facilities-idempotent-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "Test"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var campus = new Campus { TenantId = 101, Code = "A", Name = "A" };
        db.Campuses.Add(campus);
        await db.SaveChangesAsync();
        var service = new FacilitySpaceService(db, new Principal(101, "TenantAdmin"),
            TimeProvider.System, NullLogger<FacilitySpaceService>.Instance);
        var request = new SaveFacilitySpaceRequestDto { CampusId = campus.Id, Code = "ROOM-1", Name = "Hall" };
        var first = await service.SaveAsync(null, request);
        (await service.SaveAsync(null, request)).Data!.Id.Should().Be(first.Data!.Id);
        (await service.SaveAsync(null, new SaveFacilitySpaceRequestDto
            { CampusId = campus.Id, Code = "ROOM-1", Name = "Different" })).StatusCode.Should().Be(409);
        (await service.SaveAsync(first.Data.Id, request)).StatusCode.Should().Be(409);
        (await db.Set<EduOS.Core.Entities.Facilities.FacilitySpace>().CountAsync()).Should().Be(1);
    }

    private sealed class Principal(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string candidate) => candidate == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
