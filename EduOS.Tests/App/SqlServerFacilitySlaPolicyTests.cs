using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Facilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerFacilitySlaPolicyTests
{
    [Fact]
    public async Task Campus_priority_SLA_is_optional_fail_closed_and_snapshot_on_triage()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, campusId, spaceId, requesterId, managerId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-SLA-" + suffix, Name = "Facilities SLA CI",
                Email = "sla-" + suffix + "@example.test", State = TenantState.Active };
            var otherTenant = new Tenant { Code = "CI-SLX-" + suffix, Name = "SLA other tenant",
                Email = "sla-other-" + suffix + "@example.test", State = TenantState.Active };
            var requester = new ApplicationUser { UserName = "sla-r-" + suffix,
                NormalizedUserName = ("sla-r-" + suffix).ToUpperInvariant(),
                FullName = "SLA requester", Email = "sla-r-" + suffix + "@example.test",
                NormalizedEmail = ("sla-r-" + suffix + "@example.test").ToUpperInvariant(),
                EmailConfirmed = true, IsActive = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
            var manager = new ApplicationUser { UserName = "sla-m-" + suffix,
                NormalizedUserName = ("sla-m-" + suffix).ToUpperInvariant(),
                FullName = "SLA manager", Email = "sla-m-" + suffix + "@example.test",
                NormalizedEmail = ("sla-m-" + suffix + "@example.test").ToUpperInvariant(),
                EmailConfirmed = true, IsActive = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
            setup.AddRange(tenant, otherTenant, requester, manager);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = otherTenant.Id;
            requesterId = requester.Id; managerId = manager.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId,
                    Code = "SLA-" + suffix, Name = "Maintenance Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var space = new FacilitySpace { TenantId = tenantId, CampusId = campusId,
                    Code = "SLA-S-" + suffix, Name = "Facilities Repair Site" };
                setup.FacilitySpaces.Add(space);
                setup.TenantMemberships.AddRange(
                    new TenantMembership { TenantId = tenantId, UserId = managerId,
                        Status = MembershipStatus.Active },
                    new TenantMembership { TenantId = tenantId, UserId = requesterId,
                        Status = MembershipStatus.Active });
                setup.UserCampusAccesses.AddRange(
                    new UserCampusAccess { TenantId = tenantId, UserId = managerId,
                        CampusId = campusId, IsActive = true },
                    new UserCampusAccess { TenantId = tenantId, UserId = requesterId,
                        CampusId = campusId, IsActive = true });
                var role = new ApplicationRole { TenantId = tenantId,
                    Name = "FacilitiesManager", NormalizedName = "FACILITIESMANAGER", IsActive = true };
                setup.Roles.Add(role);
                await setup.SaveChangesAsync();
                setup.UserRoles.Add(new IdentityUserRole<long>
                { UserId = managerId, RoleId = role.Id });
                await setup.SaveChangesAsync();
                spaceId = space.Id;
            }
        }
        FacilityRequestDto submitted;
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var created = await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .CreateAsync(new CreateFacilityRequestDto {
                    ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                    FacilitySpaceId = spaceId, CategoryCode = "REPAIR",
                    PriorityCode = "High", Summary = "Campus breaker inspection"
                });
            Assert.True(created.Success, created.Message);
            submitted = created.Data!;
            Assert.Equal(403, (await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .SlaRecommendationAsync(submitted.Id)).StatusCode);
        }
        await using (var db = Db(options, otherTenantId, managerId, "FacilitiesManager"))
            Assert.Equal(404, (await Service(db, otherTenantId, managerId, "FacilitiesManager")
                .SlaRecommendationAsync(submitted.Id)).StatusCode is 403 or 404 ? 404 : 0);
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var manual = await service.SlaRecommendationAsync(submitted.Id);
            Assert.True(manual.Success, manual.Message);
            Assert.Null(manual.Data!.ConfiguredHours);
            Assert.Null(manual.Data.RecommendedDueUtc);
            var invalidMissing = await service.TransitionAsync(submitted.Id, "triage",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = submitted.RowVersion });
            Assert.Equal(400, invalidMissing.StatusCode);
            var policy = new TenantSetting {
                TenantId = tenantId, Key = "Facilities.SlaHours.High." + campusId,
                Value = "broken", Category = "Facilities",
                CreatedAt = DateTime.UtcNow, CreatedBy = managerId
            };
            db.TenantSettings.Add(policy);
            await db.SaveChangesAsync();
            Assert.Equal(409, (await service.SlaRecommendationAsync(submitted.Id)).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "triage",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = submitted.RowVersion,
                    SlaDueAtUtc = DateTime.UtcNow.AddHours(5) })).StatusCode);
            policy.Value = "6";
            await db.SaveChangesAsync();
            var suggested = await service.SlaRecommendationAsync(submitted.Id);
            Assert.True(suggested.Success, suggested.Message);
            Assert.Equal(6, suggested.Data!.ConfiguredHours);
            Assert.Equal(campusId, suggested.Data.CampusId);
            var requestedAt = DateTime.UtcNow;
            var command = new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                RowVersion = submitted.RowVersion };
            var triaged = await service.TransitionAsync(submitted.Id, "triage", command);
            Assert.True(triaged.Success, triaged.Message);
            Assert.True(triaged.Data!.SlaDueAtUtc >= requestedAt.AddHours(6));
            Assert.True(triaged.Data.SlaDueAtUtc <= DateTime.UtcNow.AddHours(6).AddSeconds(3));
            Assert.True((await service.TransitionAsync(submitted.Id, "triage", command)).Success);
            Assert.Equal(1, await db.FacilityWorkflowEvents.CountAsync(x =>
                x.TenantId == tenantId && x.FacilityServiceRequestId == submitted.Id &&
                x.ActionCode == "triage"));
            var events = await service.HistoryAsync(submitted.Id, 1, 25);
            Assert.True(events.Success, events.Message);
            Assert.Contains(events.Data!.Items, x => x.ActionCode == "triage" &&
                x.Note != null && x.Note.Contains("configured 6h High"));
            Assert.Equal(404, (await service.SlaRecommendationAsync(submitted.Id)).StatusCode);
        }
    }

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options,
        long tenantId, long actorId, string role)
    {
        var http = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                 new Claim(ClaimTypes.Role, role)], "Test")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static FacilityWorkflowService Service(EduOSDbContext db, long tenantId,
        long userId, string role) => new(db, new Principal(tenantId, userId, role),
            TimeProvider.System, NullLogger<FacilityWorkflowService>.Instance);

    private sealed class Principal(long tenant, long actorId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actorId;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string candidate) => role == candidate;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
