using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Facilities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerFacilityVisitorTests
{
    [Fact]
    public async Task Visitor_host_decision_badge_replay_and_checkout_are_tenant_campus_scoped()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, campusId, managerId, hostId, outsiderId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-VI-" + suffix, Name = "Visitor CI",
                Email = "visitor-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-VX-" + suffix, Name = "Other Visitor CI",
                Email = "visitor-other-" + suffix + "@example.test", State = TenantState.Active };
            var users = Enumerable.Range(1, 3).Select(i =>
            {
                var email = "visitor-" + i + "-" + suffix + "@example.test";
                return new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(),
                    FullName = "Visitor Test " + i, EmailConfirmed = true, IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            setup.AddRange(tenant, other);
            setup.AddRange(users);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            managerId = users[0].Id; hostId = users[1].Id; outsiderId = users[2].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "VIS", Name = "Visitor Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                foreach (var id in new[] { managerId, hostId })
                {
                    setup.TenantMemberships.Add(new TenantMembership
                    { TenantId = tenantId, UserId = id, Status = MembershipStatus.Active });
                    setup.UserCampusAccesses.Add(new UserCampusAccess
                    { TenantId = tenantId, CampusId = campusId, UserId = id, IsActive = true });
                }
                await setup.SaveChangesAsync();
            }
        }
        var start = DateTime.UtcNow.AddHours(1);
        var end = start.AddHours(2);
        async Task<FacilityVisitorDto> PreregisterAsync(string visitor)
        {
            await using var db = Db(options, tenantId, managerId, "FacilitiesManager");
            var result = await Service(db, tenantId, managerId, "FacilitiesManager")
                .CreateAsync(new CreateFacilityVisitorDto
                {
                    ClientRequestId = Guid.NewGuid(), CampusId = campusId, HostUserId = hostId,
                    VisitorName = visitor, Purpose = "Scheduled campus meeting",
                    VisitStartUtc = start, VisitEndUtc = end
                });
            Assert.True(result.Success, result.Message);
            return result.Data!;
        }
        var first = await PreregisterAsync("Guest One");
        var second = await PreregisterAsync("Guest Two");
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            Assert.Contains((await service.HostsAsync(campusId, null, 1, 25)).Data!.Items,
                x => x.UserId == hostId);
            Assert.Equal(403, (await service.TransitionAsync(first.Id, "hostapprove",
                new FacilityVisitorTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = first.RowVersion })).StatusCode);
        }
        await using (var db = Db(options, tenantId, outsiderId, "Staff"))
        {
            Assert.Equal(403, (await Service(db, tenantId, outsiderId, "Staff")
                .ListAsync(campusId, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, otherTenantId, outsiderId, "FacilitiesManager"))
        {
            Assert.Equal(403, (await Service(db, otherTenantId, outsiderId, "FacilitiesManager")
                .TransitionAsync(first.Id, "checkin", new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion,
                    BadgeCode = "GUEST-01" })).StatusCode);
        }
        async Task<FacilityVisitorDto> ApproveAsync(FacilityVisitorDto visit)
        {
            await using var db = Db(options, tenantId, hostId, "Staff");
            var result = await Service(db, tenantId, hostId, "Staff")
                .TransitionAsync(visit.Id, "hostapprove", new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = visit.RowVersion });
            Assert.True(result.Success, result.Message);
            return result.Data!;
        }
        first = await ApproveAsync(first);
        second = await ApproveAsync(second);
        var commandId = Guid.NewGuid();
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromMinutes(50)));
            var denied = await service.TransitionAsync(first.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = "stale", BadgeCode = "GUEST-01" });
            Assert.Equal(409, denied.StatusCode);
            var accepted = await service.TransitionAsync(first.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = commandId, RowVersion = first.RowVersion, BadgeCode = "GUEST-01" });
            Assert.True(accepted.Success, accepted.Message);
            first = accepted.Data!;
            var replay = await service.TransitionAsync(first.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = commandId, RowVersion = "stale", BadgeCode = "GUEST-01" });
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(1, await db.Set<FacilityVisitorEvent>().CountAsync(x =>
                x.FacilityVisitorVisitId == first.Id && x.ActionCode == "checkin"));
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromMinutes(50)));
            var duplicate = await service.TransitionAsync(second.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion, BadgeCode = "GUEST-01" });
            Assert.Equal(409, duplicate.StatusCode);
            var checkout = await service.TransitionAsync(first.Id, "checkout",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion });
            Assert.True(checkout.Success, checkout.Message);
            var after = await service.TransitionAsync(second.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion, BadgeCode = "GUEST-01" });
            Assert.True(after.Success, after.Message);
            Assert.Equal(1, await db.Set<FacilityVisitorVisit>().CountAsync(x =>
                x.CampusId == campusId && x.StateCode == "CheckedIn" && x.BadgeCode == "GUEST-01"));
            Assert.Equal(1, await db.Set<FacilityVisitorEvent>().CountAsync(x =>
                x.FacilityVisitorVisitId == first.Id && x.ActionCode == "checkout"));
        }
    }

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options,
        long tenantId, long userId, string role)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                 new Claim(ClaimTypes.Role, role)], "Test"))
        };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static FacilityVisitorService Service(EduOSDbContext db, long tenantId, long userId,
        string role, TimeProvider? time = null) =>
        new(db, new Principal(tenantId, userId, role), time ?? TimeProvider.System,
            NullLogger<FacilityVisitorService>.Instance);
    private sealed class ShiftedClock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }
    private sealed class Principal(long tenant, long id, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
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
