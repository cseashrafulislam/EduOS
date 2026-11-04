using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Facilities;
using EduOS.Core.Entities.Inventory;
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
public sealed class SqlServerFacilityVendorServiceAcceptanceTests
{
    [Fact]
    public async Task Vendor_service_requires_completed_work_independent_review_and_no_invented_payable()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, campusId, requestId, managerId, reviewerId, requesterId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-FVE-" + suffix, Name = "Vendor Evidence CI",
                Email = "vendor-evidence-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-FVX-" + suffix, Name = "Other Vendor Evidence CI",
                Email = "other-evidence-" + suffix + "@example.test", State = TenantState.Active };
            var actors = Enumerable.Range(1, 3).Select(n => {
                var username = "vendor-evidence-" + n + "-" + suffix;
                var email = username + "@example.test";
                return new ApplicationUser { UserName = username,
                    NormalizedUserName = username.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(),
                    FullName = "Facilities Service CI", EmailConfirmed = true, IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            setup.AddRange(tenant, other); setup.AddRange(actors);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            requesterId = actors[0].Id; managerId = actors[1].Id; reviewerId = actors[2].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "VND-" + suffix,
                    Name = "Campus Vendor Repairs" };
                setup.Campuses.Add(campus);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var space = new FacilitySpace { TenantId = tenantId, CampusId = campusId,
                    Code = "WORK-" + suffix, Name = "Electrical Workshop" };
                var vendor = new ProcurementSupplier { TenantId = tenantId, CampusId = campusId,
                    ClientRequestId = Guid.NewGuid(), SupplierCode = "SVC-" + suffix,
                    Name = "Approved Service Supplier", StateCode = "Approved" };
                setup.AddRange(space, vendor);
                var managerRole = new ApplicationRole { TenantId = tenantId,
                    Name = "FacilitiesManager", NormalizedName = "FACILITIESMANAGER", IsActive = true };
                var reviewerRole = new ApplicationRole { TenantId = tenantId,
                    Name = "FacilitiesApprover", NormalizedName = "FACILITIESAPPROVER", IsActive = true };
                setup.Roles.AddRange(managerRole, reviewerRole);
                foreach (var id in new[] { managerId, reviewerId, requesterId })
                {
                    setup.TenantMemberships.Add(new TenantMembership {
                        TenantId = tenantId, UserId = id, Status = MembershipStatus.Active });
                    setup.UserCampusAccesses.Add(new UserCampusAccess {
                        TenantId = tenantId, UserId = id, CampusId = campusId, IsActive = true });
                }
                await setup.SaveChangesAsync();
                setup.UserRoles.AddRange(
                    new IdentityUserRole<long> { UserId = managerId, RoleId = managerRole.Id },
                    new IdentityUserRole<long> { UserId = reviewerId, RoleId = reviewerRole.Id });
                var request = new FacilityServiceRequest { TenantId = tenantId,
                    CampusId = campusId, FacilitySpaceId = space.Id, ClientRequestId = Guid.NewGuid(),
                    RequesterUserId = requesterId, CategoryCode = "REPAIR", PriorityCode = "Normal",
                    Summary = "Repair critical generator", StateCode = "Approved",
                    ApprovedByUserId = reviewerId, ApprovedAtUtc = DateTime.UtcNow };
                setup.FacilityServiceRequests.Add(request);
                await setup.SaveChangesAsync();
                requestId = request.Id;
                setup.FacilityWorkOrders.Add(new FacilityWorkOrder {
                    TenantId = tenantId, FacilityServiceRequestId = requestId,
                    StateCode = "Approved", VendorSupplierId = vendor.Id,
                    VendorStateCode = "Approved", VendorProposedByUserId = managerId,
                    VendorApprovedByUserId = reviewerId,
                    VendorStartUtc = DateTime.UtcNow.AddHours(-5),
                    VendorEndUtc = DateTime.UtcNow.AddHours(2) });
                await setup.SaveChangesAsync();
            }
        }
        FacilityRequestDto work;
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            work = (await service.ListAsync(campusId, null, 1, 25)).Data!.Items.Single(x => x.Id == requestId);
            Assert.Equal("None", work.VendorAcceptanceStateCode);
            Assert.Equal(409, (await service.TransitionAsync(requestId, "vendorattest",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = work.WorkOrderRowVersion!,
                    Note = "Vendor says work was completed before technician approval." })).StatusCode);
            Assert.Equal(0, await db.FacilityWorkflowEvents.CountAsync(x =>
                x.TenantId == tenantId && x.FacilityServiceRequestId == requestId &&
                x.ActionCode == "vendorattest"));
            var order = await db.FacilityWorkOrders.SingleAsync(x => x.FacilityServiceRequestId == requestId);
            order.StateCode = "PendingVerification";
            order.StartedAtUtc = DateTime.UtcNow.AddHours(-4);
            order.CompletedAtUtc = DateTime.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
            work = (await service.ListAsync(campusId, null, 1, 25)).Data!.Items.Single(x => x.Id == requestId);
            var command = new FacilityTransitionDto {
                ClientCommandId = Guid.NewGuid(), RowVersion = work.WorkOrderRowVersion!,
                Note = "Service technician certified generator repair and electrical test."
            };
            var submitted = await service.TransitionAsync(requestId, "vendorattest", command);
            Assert.True(submitted.Success, submitted.Message);
            work = submitted.Data!;
            Assert.Equal("Submitted", work.VendorAcceptanceStateCode);
            Assert.True((await service.TransitionAsync(requestId, "vendorattest", command)).Success);
            Assert.Equal(409, (await service.TransitionAsync(requestId, "vendorattest",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = work.WorkOrderRowVersion!,
                    Note = "Duplicate attestation must not replace pending evidence." })).StatusCode);
            Assert.Equal(1, await db.FacilityWorkflowEvents.CountAsync(x =>
                x.TenantId == tenantId && x.FacilityServiceRequestId == requestId &&
                x.ActionCode == "vendorattest"));
        }
        await using (var db = Db(options, otherTenantId, reviewerId, "FacilitiesApprover"))
            Assert.Equal(404, (await Service(db, otherTenantId, reviewerId, "FacilitiesApprover")
                .TransitionAsync(requestId, "vendorverify",
                    new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                        RowVersion = work.WorkOrderRowVersion!,
                        Note = "Cross tenant decision must never reveal vendor evidence." })).StatusCode);
        await using (var db = Db(options, tenantId, reviewerId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, reviewerId, "FacilitiesApprover");
            Assert.Equal(409, (await service.TransitionAsync(requestId, "vendorverify",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = "stale",
                    Note = "Stale verification cannot certify supplier work." })).StatusCode);
            var returned = await service.TransitionAsync(requestId, "vendorreturn",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = work.WorkOrderRowVersion!,
                    Note = "Service evidence lacks independent electrical safety reading." });
            Assert.True(returned.Success, returned.Message);
            work = returned.Data!;
            Assert.Equal("Returned", work.VendorAcceptanceStateCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var correction = await service.TransitionAsync(requestId, "vendorattest",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = work.WorkOrderRowVersion!,
                    Note = "Corrected service certificate includes generator load and safety test." });
            Assert.True(correction.Success, correction.Message);
            work = correction.Data!;
            Assert.Equal("Submitted", work.VendorAcceptanceStateCode);
        }
        await using (var db = Db(options, tenantId, reviewerId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, reviewerId, "FacilitiesApprover");
            var review = new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                RowVersion = work.WorkOrderRowVersion!,
                Note = "Independently inspected service completion evidence and verified work." };
            var verified = await service.TransitionAsync(requestId, "vendorverify", review);
            Assert.True(verified.Success, verified.Message);
            Assert.Equal("Verified", verified.Data!.VendorAcceptanceStateCode);
            Assert.True((await service.TransitionAsync(requestId, "vendorverify", review)).Success);
            Assert.Equal(1, await db.FacilityWorkflowEvents.CountAsync(x =>
                x.TenantId == tenantId && x.FacilityServiceRequestId == requestId &&
                x.ActionCode == "vendorverify"));
            Assert.Equal(0, await db.Journals.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(0, await db.ProcurementSupplierInvoices.CountAsync(x => x.TenantId == tenantId));
            var history = await service.HistoryAsync(requestId, 1, 25);
            Assert.True(history.Success, history.Message);
            Assert.Contains(history.Data!.Items, x => x.ActionCode == "vendorverify");
        }
    }

    private static EduOSDbContext Db(DbContextOptions<EduOSDbContext> options,
        long tenantId, long actorId, string role)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                new Claim(ClaimTypes.Role, role)], "Test")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static FacilityWorkflowService Service(EduOSDbContext db, long tenantId, long userId, string role) =>
        new(db, new Principal(tenantId, userId, role), TimeProvider.System,
            NullLogger<FacilityWorkflowService>.Instance);

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
        public bool IsInRole(string candidate) => candidate == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
