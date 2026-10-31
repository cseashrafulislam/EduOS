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
public sealed class SqlServerFacilityWorkOrderTests
{
    [Fact]
    public async Task Independent_approval_creates_one_order_and_technician_closure_is_audited()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, campusId, spaceId;
        long requesterId, managerId, approverId, techId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-FW-" + suffix, Name = "Facilities CI",
                Email = "facility-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-FX-" + suffix, Name = "Other Facilities CI",
                Email = "other-facility-" + suffix + "@example.test", State = TenantState.Active };
            var users = Enumerable.Range(1, 4).Select(i =>
            {
                var email = "facility-" + i + "-" + suffix + "@example.test";
                return new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), FullName = "Facilities Test",
                    EmailConfirmed = true, IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            setup.AddRange(tenant, other);
            setup.AddRange(users);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            requesterId = users[0].Id; managerId = users[1].Id;
            approverId = users[2].Id; techId = users[3].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "FAC", Name = "Facilities Campus" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var space = new FacilitySpace { TenantId = tenantId, CampusId = campusId,
                    Code = "BLDG-A", Name = "Building A" };
                setup.FacilitySpaces.Add(space);
                foreach (var id in new[] { requesterId, managerId, approverId, techId })
                    setup.UserCampusAccesses.Add(new UserCampusAccess
                    { TenantId = tenantId, CampusId = campusId, UserId = id, IsActive = true });
                setup.TenantMemberships.Add(new TenantMembership
                { TenantId = tenantId, UserId = techId, Status = MembershipStatus.Active });
                await setup.SaveChangesAsync();
                spaceId = space.Id;
            }
        }
        // Same-campus membership must not expose a different requester's details.
        long privateRequestId;
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var created = await Service(db, tenantId, managerId, "FacilitiesManager")
                .CreateAsync(new CreateFacilityRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), CampusId = campusId, FacilitySpaceId = spaceId,
                    CategoryCode = "PLUMBING", PriorityCode = "Normal", Summary = "Private manager repair"
                });
            Assert.True(created.Success, created.Message);
            privateRequestId = created.Data!.Id;
        }
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var service = Service(db, tenantId, requesterId, "FacilitiesRequester");
            Assert.Empty((await service.ListAsync(null, null, 1, 25)).Data!.Items);
            Assert.Equal(404, (await service.HistoryAsync(privateRequestId, 1, 25)).StatusCode);
            Assert.Equal(0, (await service.DashboardAsync(null)).Data!.TotalRequests);
        }
        var clientId = Guid.NewGuid();
        FacilityRequestDto submitted;
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var service = Service(db, tenantId, requesterId, "FacilitiesRequester");
            var created = await service.CreateAsync(new CreateFacilityRequestDto
            {
                ClientRequestId = clientId, CampusId = campusId, FacilitySpaceId = spaceId,
                CategoryCode = "ELECTRICAL", PriorityCode = "High", Summary = "Broken hallway light"
            });
            Assert.Equal(201, created.StatusCode);
            submitted = created.Data!;
            var replay = await service.CreateAsync(new CreateFacilityRequestDto
            {
                ClientRequestId = clientId, CampusId = campusId, FacilitySpaceId = spaceId,
                CategoryCode = "ELECTRICAL", PriorityCode = "High", Summary = "Broken hallway light"
            });
            Assert.Equal(submitted.Id, replay.Data!.Id);
        }
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var service = Service(db, tenantId, requesterId, "FacilitiesRequester");
            var visible = await service.ListAsync(null, null, 1, 25);
            Assert.Single(visible.Data!.Items);
            Assert.Equal(submitted.Id, visible.Data.Items[0].Id);
            Assert.Equal(1, (await service.DashboardAsync(null)).Data!.TotalRequests);
            Assert.Equal(404, (await service.HistoryAsync(privateRequestId, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            Assert.Equal(2, (await Service(db, tenantId, managerId, "FacilitiesManager")
                .DashboardAsync(null)).Data!.TotalRequests);
        }
        FacilityRequestDto triaged;
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var result = await service.TransitionAsync(submitted.Id, "triage", new FacilityTransitionDto
            {
                ClientCommandId = Guid.NewGuid(), RowVersion = submitted.RowVersion,
                SlaDueAtUtc = DateTime.UtcNow.AddHours(4)
            });
            Assert.True(result.Success, result.Message);
            triaged = result.Data!;
        }
        FacilityRequestDto approved;
        var approvalId = Guid.NewGuid();
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover");
            var result = await service.TransitionAsync(submitted.Id, "approve", new FacilityTransitionDto
            { ClientCommandId = approvalId, RowVersion = triaged.RowVersion });
            Assert.True(result.Success, result.Message);
            approved = result.Data!;
            Assert.NotNull(approved.WorkOrderId);
            Assert.Equal("Approved", approved.WorkOrderState);
            var replay = await service.TransitionAsync(submitted.Id, "approve", new FacilityTransitionDto
            { ClientCommandId = approvalId, RowVersion = triaged.RowVersion });
            Assert.True(replay.Success, replay.Message);
            var stale = await service.TransitionAsync(submitted.Id, "approve", new FacilityTransitionDto
            { ClientCommandId = Guid.NewGuid(), RowVersion = triaged.RowVersion });
            Assert.Equal(409, stale.StatusCode);
        }
        FacilityRequestDto scheduled;
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var result = await Service(db, tenantId, managerId, "FacilitiesManager").TransitionAsync(
                submitted.Id, "schedule", new FacilityTransitionDto
                {
                    ClientCommandId = Guid.NewGuid(), RowVersion = approved.WorkOrderRowVersion!,
                    TechnicianUserId = techId, ScheduledStartUtc = DateTime.UtcNow.AddMinutes(15),
                    ScheduledEndUtc = DateTime.UtcNow.AddHours(2)
                });
            Assert.True(result.Success, result.Message);
            scheduled = result.Data!;
        }
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var service = Service(db, tenantId, techId, "FacilitiesTechnician");
            var visible = await service.ListAsync(null, null, 1, 25);
            Assert.Single(visible.Data!.Items);
            Assert.Equal(submitted.Id, visible.Data.Items[0].Id);
            Assert.Equal(404, (await service.HistoryAsync(privateRequestId, 1, 25)).StatusCode);
        }
        FacilityRequestDto started;
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var result = await Service(db, tenantId, techId, "FacilitiesTechnician").TransitionAsync(
                submitted.Id, "start", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion! });
            Assert.True(result.Success, result.Message);
            started = result.Data!;
        }
        FacilityRequestDto completed;
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var result = await Service(db, tenantId, techId, "FacilitiesTechnician").TransitionAsync(
                submitted.Id, "complete", new FacilityTransitionDto
                {
                    ClientCommandId = Guid.NewGuid(), RowVersion = started.WorkOrderRowVersion!,
                    Note = "Replaced the hallway light and verified the circuit."
                });
            Assert.True(result.Success, result.Message);
            completed = result.Data!;
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover");
            var result = await service.TransitionAsync(submitted.Id, "verify", new FacilityTransitionDto
            {
                ClientCommandId = Guid.NewGuid(), RowVersion = completed.WorkOrderRowVersion!,
                Note = "Inspected working light on site."
            });
            Assert.True(result.Success, result.Message);
            Assert.Equal("Closed", result.Data!.WorkOrderState);
            Assert.NotNull(result.Data.VerifiedAtUtc);
            var history = await service.HistoryAsync(submitted.Id, 1, 25);
            Assert.Equal(7, history.Data!.TotalCount);
            Assert.Equal("verify", history.Data.Items.Last().ActionCode);
            Assert.Single(await db.FacilityWorkOrders.Where(x => x.FacilityServiceRequestId == submitted.Id).ToListAsync());
        }
        await using (var db = Db(options, otherTenantId, requesterId, "FacilitiesRequester"))
        {
            var service = Service(db, otherTenantId, requesterId, "FacilitiesRequester");
            Assert.Empty((await service.ListAsync(null, null, 1, 25)).Data!.Items);
            Assert.Equal(404, (await service.HistoryAsync(submitted.Id, 1, 25)).StatusCode);
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

    private static FacilityWorkflowService Service(EduOSDbContext db, long tenantId, long userId, string role) =>
        new(db, new Principal(tenantId, userId, role), TimeProvider.System,
            NullLogger<FacilityWorkflowService>.Instance);

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