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
        long requesterId, managerId, approverId, techId, globalTechId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-FW-" + suffix, Name = "Facilities CI",
                Email = "facility-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-FX-" + suffix, Name = "Other Facilities CI",
                Email = "other-facility-" + suffix + "@example.test", State = TenantState.Active };
            var users = Enumerable.Range(1, 5).Select(i =>
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
            globalTechId = users[4].Id;
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
                // An active same-tenant campus without a grant must not be
                // discoverable by a requester, even through the location picker.
                var ungrantedCampus = new Campus { TenantId = tenantId,
                    Code = "PRIVATE", Name = "Restricted Campus" };
                setup.Campuses.Add(ungrantedCampus);
                await setup.SaveChangesAsync();
                setup.FacilitySpaces.Add(new FacilitySpace { TenantId = tenantId,
                    CampusId = ungrantedCampus.Id, Code = "SECRET",
                    Name = "Restricted Storage" });
                foreach (var id in new[] { requesterId, managerId, approverId, techId, globalTechId })
                    setup.UserCampusAccesses.Add(new UserCampusAccess
                    { TenantId = tenantId, CampusId = campusId, UserId = id, IsActive = true });
                // A campus-granted active member without the technician role must
                // not be assignable, even by a Facilities manager.
                setup.TenantMemberships.AddRange(
                    new TenantMembership
                    { TenantId = tenantId, UserId = techId, Status = MembershipStatus.Active },
                    new TenantMembership
                    { TenantId = tenantId, UserId = managerId, Status = MembershipStatus.Active },
                    new TenantMembership
                    { TenantId = tenantId, UserId = globalTechId, Status = MembershipStatus.Active });
                var technicianRole = new ApplicationRole
                {
                    TenantId = tenantId, Name = "FacilitiesTechnician",
                    NormalizedName = "FACILITIESTECHNICIAN", IsActive = true
                };
                var globalTechnicianRole = new ApplicationRole
                {
                    TenantId = null, Name = "FacilitiesTechnician",
                    NormalizedName = "FACILITIESTECHNICIAN", IsActive = true, IsSystemRole = false
                };
                var secondFacilitiesApproverRole = new ApplicationRole
                {
                    TenantId = tenantId, Name = "FacilitiesApprover",
                    NormalizedName = "FACILITIESAPPROVER", IsActive = true
                };
                setup.Roles.AddRange(technicianRole, globalTechnicianRole, secondFacilitiesApproverRole);
                await setup.SaveChangesAsync();
                setup.UserRoles.Add(new IdentityUserRole<long>
                { UserId = techId, RoleId = technicianRole.Id });
                setup.UserRoles.Add(new IdentityUserRole<long>
                { UserId = globalTechId, RoleId = globalTechnicianRole.Id });
                setup.UserRoles.Add(new IdentityUserRole<long>
                { UserId = globalTechId, RoleId = secondFacilitiesApproverRole.Id });
                await setup.SaveChangesAsync();
                spaceId = space.Id;
                setup.ProcurementSuppliers.AddRange(
                    new ProcurementSupplier { TenantId = tenantId, CampusId = campusId,
                        ClientRequestId = Guid.NewGuid(), SupplierCode = "FAC-APP-" + suffix,
                        Name = "Approved Facilities Vendor", StateCode = "Approved" },
                    new ProcurementSupplier { TenantId = tenantId, CampusId = campusId,
                        ClientRequestId = Guid.NewGuid(), SupplierCode = "FAC-PEND-" + suffix,
                        Name = "Pending Facilities Vendor", StateCode = "Submitted" },
                    new ProcurementSupplier { TenantId = tenantId, CampusId = ungrantedCampus.Id,
                        ClientRequestId = Guid.NewGuid(), SupplierCode = "FAC-SECRET-" + suffix,
                        Name = "Private Facilities Vendor", StateCode = "Approved" });
                await setup.SaveChangesAsync();
            }
            // A second Suspended membership still makes a global legacy role ambiguous.
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(otherTenantId))
            {
                setup.TenantMemberships.Add(new TenantMembership
                { TenantId = otherTenantId, UserId = globalTechId,
                    Status = MembershipStatus.Suspended });
                await setup.SaveChangesAsync();
            }
        }
        // Requesters can discover only their authorized active campus spaces,
        // while a TenantAdmin can inspect all active spaces in the same tenant.
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var service = Service(db, tenantId, requesterId, "FacilitiesRequester");
            var locations = await service.LocationsAsync(null, 1, 25);
            Assert.True(locations.Success, locations.Message);
            Assert.Single(locations.Data!.Items);
            Assert.Equal(campusId, locations.Data.Items[0].CampusId);
            Assert.Equal(spaceId, locations.Data.Items[0].FacilitySpaceId);
            Assert.Empty((await service.LocationsAsync("Restricted", 1, 25)).Data!.Items);
            Assert.Single((await service.LocationsAsync("Building", 1, 25)).Data!.Items);
            Assert.Equal(400, (await service.LocationsAsync(new string('X', 101), 1, 25)).StatusCode);
        }
        await using (var db = Db(options, tenantId, requesterId, "TenantAdmin"))
        {
            var locations = await Service(db, tenantId, requesterId, "TenantAdmin")
                .LocationsAsync(null, 1, 25);
            Assert.Equal(2, locations.Data!.TotalCount);
        }
        await using (var db = Db(options, otherTenantId, requesterId, "FacilitiesRequester"))
        {
            Assert.Empty((await Service(db, otherTenantId, requesterId, "FacilitiesRequester")
                .LocationsAsync(null, 1, 25)).Data!.Items);
        }
        // Manager-only lookup follows active tenant membership, campus and role grants.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var eligible = await service.TechniciansAsync(campusId, null, 1, 25);
            Assert.True(eligible.Success, eligible.Message);
            Assert.Single(eligible.Data!.Items);
            Assert.Equal(techId, eligible.Data.Items[0].UserId);
            var vendors = await service.VendorsAsync(campusId, null, 1, 25);
            Assert.True(vendors.Success, vendors.Message);
            Assert.Single(vendors.Data!.Items);
            Assert.Equal("Approved Facilities Vendor", vendors.Data.Items[0].SupplierName);
            Assert.StartsWith("FAC-APP-", vendors.Data.Items[0].SupplierCode);
            Assert.Single((await service.VendorsAsync(campusId, "Approved", 1, 25)).Data!.Items);
            Assert.Empty((await service.VendorsAsync(campusId, "Pending", 1, 25)).Data!.Items);
            Assert.Equal(403, (await service.VendorsAsync(campusId + 1, null, 1, 25)).StatusCode);
            Assert.Equal(400, (await service.VendorsAsync(campusId, new string('X', 101), 1, 25)).StatusCode);
            Assert.Equal(400, (await service.VendorsAsync(campusId, null, 1, 51)).StatusCode);
            Assert.DoesNotContain(eligible.Data.Items, x => x.UserId == globalTechId);
            Assert.Equal("Facilities Test", eligible.Data.Items[0].DisplayName);
            Assert.Single((await service.TechniciansAsync(campusId, "Facilities", 1, 25)).Data!.Items);
            Assert.Empty((await service.TechniciansAsync(campusId, "Unknown", 1, 25)).Data!.Items);
            Assert.Equal(400, (await service.TechniciansAsync(campusId,
                new string('X', 101), 1, 25)).StatusCode);
            Assert.Equal(403, (await service.TechniciansAsync(campusId + 1, null, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            Assert.Equal(403, (await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .TechniciansAsync(campusId, null, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, otherTenantId, managerId, "FacilitiesManager"))
        {
            Assert.Equal(403, (await Service(db, otherTenantId, managerId, "FacilitiesManager")
                .TechniciansAsync(campusId, null, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            Assert.Equal(403, (await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .VendorsAsync(campusId, null, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, otherTenantId, managerId, "FacilitiesManager"))
        {
            Assert.Equal(403, (await Service(db, otherTenantId, managerId, "FacilitiesManager")
                .VendorsAsync(campusId, null, 1, 25)).StatusCode);
        }
        // A departed second-tenant membership restores global-role eligibility;
        // returning it to Suspended must revoke that eligibility again.
        await using (var db = Db(options, otherTenantId, globalTechId, "TenantAdmin"))
        {
            var membership = await db.TenantMemberships.SingleAsync(x =>
                x.TenantId == otherTenantId && x.UserId == globalTechId);
            membership.Status = MembershipStatus.Left;
            await db.SaveChangesAsync();
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var eligible = await Service(db, tenantId, managerId, "FacilitiesManager")
                .TechniciansAsync(campusId, null, 1, 25);
            Assert.Contains(eligible.Data!.Items, x => x.UserId == globalTechId);
            Assert.Contains(eligible.Data.Items, x => x.UserId == techId);
        }
        await using (var db = Db(options, otherTenantId, globalTechId, "TenantAdmin"))
        {
            var membership = await db.TenantMemberships.SingleAsync(x =>
                x.TenantId == otherTenantId && x.UserId == globalTechId);
            membership.Status = MembershipStatus.Suspended;
            await db.SaveChangesAsync();
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
            var triageId = Guid.NewGuid();
            var due = DateTime.UtcNow.AddHours(4);
            FacilityTransitionDto Triage(DateTime deadline, string? reason = null) => new()
            {
                ClientCommandId = triageId, RowVersion = submitted.RowVersion,
                SlaDueAtUtc = deadline, Note = reason
            };
            var result = await service.TransitionAsync(submitted.Id, "triage", Triage(due));
            Assert.True(result.Success, result.Message);
            triaged = result.Data!;
            Assert.True((await service.TransitionAsync(submitted.Id, "triage", Triage(due))).Success);
            Assert.Equal(409, (await service.TransitionAsync(
                submitted.Id, "triage", Triage(due.AddMinutes(30)))).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(
                submitted.Id, "triage", Triage(due, "Altered deadline reason"))).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "triage",
                Triage(due, $"SLA due UTC={due:O}"))).StatusCode);
            Assert.Single((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "triage"));
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
        long approvedVendorId;
        // Interactive vendor preview must bind to an approved work order. The
        // campus-only lookup remains available for existing read-only consumers.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var scoped = await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: approved.WorkOrderId);
            Assert.True(scoped.Success, scoped.Message);
            Assert.Single(scoped.Data!.Items);
            approvedVendorId = scoped.Data.Items[0].SupplierId;
            Assert.Equal(400, (await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: 0)).StatusCode);
            Assert.Equal(403, (await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: long.MaxValue)).StatusCode);
            Assert.Equal(403, (await service.VendorsAsync(campusId + 1, null, 1, 25,
                workOrderId: approved.WorkOrderId)).StatusCode);
        }
        // A time-filtered vendor preview requires a valid UTC window and an
        // authorized order; it must not silently become a booking.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var from = DateTime.UtcNow.AddHours(2);
            var until = from.AddHours(1);
            Assert.Equal(400, (await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: approved.WorkOrderId, startUtc: from)).StatusCode);
            Assert.Equal(400, (await service.VendorsAsync(campusId, null, 1, 25,
                startUtc: from, endUtc: until)).StatusCode);
            Assert.Equal(400, (await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: approved.WorkOrderId, startUtc: from.AddHours(-5),
                endUtc: until)).StatusCode);
            Assert.Equal(403, (await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: long.MaxValue, startUtc: from, endUtc: until)).StatusCode);
            Assert.Single((await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: approved.WorkOrderId, startUtc: from, endUtc: until)).Data!.Items);
        }
        // The vendor proposal is durable but does not book until an independent
        // approver rechecks supplier status and competing approved windows.
        FacilityRequestDto vendorProposal;
        var vendorStart = DateTime.UtcNow.AddHours(3);
        var vendorEnd = vendorStart.AddHours(1);
        var vendorCommand = Guid.NewGuid();
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var invalid = await service.TransitionAsync(submitted.Id, "vendorpropose",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = approved.WorkOrderRowVersion!,
                    VendorSupplierId = long.MaxValue, VendorStartUtc = vendorStart,
                    VendorEndUtc = vendorEnd, Note = "Vendor preflight must fail closed." });
            Assert.Equal(403, invalid.StatusCode);
            var proposal = new FacilityTransitionDto { ClientCommandId = vendorCommand,
                RowVersion = approved.WorkOrderRowVersion!, VendorSupplierId = approvedVendorId,
                VendorStartUtc = vendorStart, VendorEndUtc = vendorEnd,
                Note = "Approved campus supplier requested for electrical maintenance." };
            var created = await service.TransitionAsync(submitted.Id, "vendorpropose", proposal);
            Assert.True(created.Success, created.Message);
            vendorProposal = created.Data!;
            Assert.Equal("Proposed", vendorProposal.VendorStateCode);
            Assert.Equal(approvedVendorId, vendorProposal.VendorSupplierId);
            Assert.True((await service.TransitionAsync(submitted.Id, "vendorpropose", proposal)).Success);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "vendorpropose",
                new FacilityTransitionDto { ClientCommandId = vendorCommand,
                    RowVersion = approved.WorkOrderRowVersion!, VendorSupplierId = approvedVendorId,
                    VendorStartUtc = vendorStart.AddMinutes(5), VendorEndUtc = vendorEnd,
                    Note = proposal.Note })).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "vendorpropose",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = approved.WorkOrderRowVersion!, VendorSupplierId = approvedVendorId,
                    VendorStartUtc = vendorStart, VendorEndUtc = vendorEnd,
                    Note = proposal.Note })).StatusCode);
        }
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            Assert.Equal(403, (await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .TransitionAsync(submitted.Id, "vendorapprove", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = vendorProposal.WorkOrderRowVersion!,
                  Note = "Requester cannot authorize a supplier booking." })).StatusCode);
            Assert.Equal(403, (await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .VendorsAsync(campusId, null, 1, 25, workOrderId: approved.WorkOrderId)).StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "TenantAdmin"))
        {
            Assert.Equal(403, (await Service(db, tenantId, managerId, "TenantAdmin")
                .TransitionAsync(submitted.Id, "vendorapprove", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = vendorProposal.WorkOrderRowVersion!,
                  Note = "Vendor proposal maker must not self approve." })).StatusCode);
        }
        // A late decision and a supplier revoked after preview must both fail
        // without mutating the proposal or emitting an approval event.
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var late = await Service(db, tenantId, approverId, "FacilitiesApprover",
                new ShiftedClock(TimeSpan.FromHours(5)))
                .TransitionAsync(submitted.Id, "vendorapprove", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = vendorProposal.WorkOrderRowVersion!,
                  Note = "Expired vendor booking must not be approved." });
            Assert.Equal(409, late.StatusCode);
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            await db.ProcurementSuppliers.Where(x => x.Id == approvedVendorId)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.StateCode, "Inactive"));
            var denied = await Service(db, tenantId, approverId, "FacilitiesApprover")
                .TransitionAsync(submitted.Id, "vendorapprove", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = vendorProposal.WorkOrderRowVersion!,
                  Note = "Inactive vendor cannot be booked after proposal." });
            Assert.Equal(409, denied.StatusCode);
            Assert.Equal(0, await db.Set<FacilityWorkflowEvent>().CountAsync(x =>
                x.FacilityServiceRequestId == submitted.Id && x.ActionCode == "vendorapprove"));
            await db.ProcurementSuppliers.Where(x => x.Id == approvedVendorId)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.StateCode, "Approved"));
        }
        // Supplier lock/overlap queries can outlast the proposed booking window.
        // A synthetic third clock read expires immediately before SaveChanges.
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var drift = await Service(db, tenantId, approverId, "FacilitiesApprover",
                new StepClock(3, TimeSpan.FromHours(5)))
                .TransitionAsync(submitted.Id, "vendorapprove", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = vendorProposal.WorkOrderRowVersion!,
                  Note = "Vendor booking expired after initial SQL validation." });
            Assert.Equal(409, drift.StatusCode);
            Assert.Equal("Proposed", (await db.FacilityWorkOrders.AsNoTracking()
                .SingleAsync(x => x.FacilityServiceRequestId == submitted.Id)).VendorStateCode);
            Assert.Equal(0, await db.FacilityWorkflowEvents.CountAsync(x =>
                x.FacilityServiceRequestId == submitted.Id && x.ActionCode == "vendorapprove"));
        }
        FacilityRequestDto vendorBooked;
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover");
            var command = new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                RowVersion = vendorProposal.WorkOrderRowVersion!,
                Note = "Independent campus vendor booking approved." };
            var accepted = await service.TransitionAsync(submitted.Id, "vendorapprove", command);
            Assert.True(accepted.Success, accepted.Message);
            vendorBooked = accepted.Data!;
            Assert.Equal("Approved", vendorBooked.VendorStateCode);
            Assert.True((await service.TransitionAsync(submitted.Id, "vendorapprove", command)).Success);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "vendorapprove",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = vendorProposal.WorkOrderRowVersion!,
                    Note = "Stale vendor approval cannot be replayed." })).StatusCode);
        }
        await using (var db = Db(options, tenantId, globalTechId, "FacilitiesApprover"))
        {
            var released = await Service(db, tenantId, globalTechId, "FacilitiesApprover")
                .TransitionAsync(submitted.Id, "vendorrelease", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = vendorBooked.WorkOrderRowVersion!,
                  Note = "Independent release before maintenance has started." });
            Assert.True(released.Success, released.Message);
            approved = released.Data!;
            Assert.Equal("Released", approved.VendorStateCode);
        }
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var service = Service(db, tenantId, requesterId, "FacilitiesRequester");
            var visible = (await service.ListAsync(campusId, null, 1, 25)).Data!.Items
                .Single(x => x.Id == submitted.Id);
            Assert.Null(visible.VendorSupplierId);
            Assert.Null(visible.VendorStartUtc);
            var events = (await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode.StartsWith("vendor")).ToArray();
            Assert.Equal(3, events.Length);
            Assert.All(events, x => Assert.Equal("Vendor decision recorded.", x.Note));
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            Assert.Equal(3, await db.Set<FacilityWorkflowEvent>().CountAsync(x =>
                x.FacilityServiceRequestId == submitted.Id && x.ActionCode.StartsWith("vendor")));
            Assert.Empty(await db.Set<FacilityWorkOrder>().Where(x =>
                x.FacilityServiceRequestId == submitted.Id &&
                x.VendorStateCode == "Approved").ToListAsync());
        }
        await using (var db = Db(options, otherTenantId, managerId, "FacilitiesManager"))
        {
            Assert.Equal(403, (await Service(db, otherTenantId, managerId, "FacilitiesManager")
                .VendorsAsync(campusId, null, 1, 25, workOrderId: approved.WorkOrderId)).StatusCode);
        }
        // A global legacy role cannot bypass canonical tenant-role resolution when
        // another non-Left institution membership is Suspended.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var rejected = await service.TransitionAsync(submitted.Id, "schedule",
                new FacilityTransitionDto
                {
                    ClientCommandId = Guid.NewGuid(), RowVersion = approved.WorkOrderRowVersion!,
                    TechnicianUserId = globalTechId, ScheduledStartUtc = DateTime.UtcNow.AddMinutes(15),
                    ScheduledEndUtc = DateTime.UtcNow.AddHours(2)
                });
            Assert.Equal(403, rejected.StatusCode);
            Assert.DoesNotContain((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items,
                x => x.ActionCode == "schedule");
            Assert.Equal("Approved", (await service.ListAsync(campusId, null, 1, 25))
                .Data!.Items.Single(x => x.Id == submitted.Id).WorkOrderState);
        }
        // A campus member without the technician role cannot receive a work order.
        // Denial leaves both order state and immutable history unchanged.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var unauthorized = await service.TransitionAsync(submitted.Id, "schedule",
                new FacilityTransitionDto
                {
                    ClientCommandId = Guid.NewGuid(), RowVersion = approved.WorkOrderRowVersion!,
                    TechnicianUserId = managerId, ScheduledStartUtc = DateTime.UtcNow.AddMinutes(15),
                    ScheduledEndUtc = DateTime.UtcNow.AddHours(2)
                });
            Assert.Equal(403, unauthorized.StatusCode);
            Assert.DoesNotContain((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items,
                x => x.ActionCode == "schedule");
            var unchanged = (await service.ListAsync(campusId, null, 1, 25)).Data!.Items
                .Single(x => x.Id == submitted.Id);
            Assert.Equal("Approved", unchanged.WorkOrderState);
            Assert.Null(unchanged.TechnicianUserId);
        }
        FacilityRequestDto scheduled;
        var scheduleCommandId = Guid.NewGuid();
        var scheduleStart = DateTime.UtcNow.AddMinutes(15);
        var scheduleEnd = DateTime.UtcNow.AddHours(2);
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            FacilityTransitionDto ScheduleRetry(long technician, DateTime start, DateTime end, string? note = null)
                => new()
                {
                    ClientCommandId = scheduleCommandId, RowVersion = approved.WorkOrderRowVersion!,
                    TechnicianUserId = technician, ScheduledStartUtc = start, ScheduledEndUtc = end,
                    Note = note
                };
            var originalScheduleNote = "Campus reschedule; reason=embedded";
            // A delayed command or stale availability preview must not backdate
            // technician work. The injected clock keeps this regression deterministic.
            var lateClock = new ShiftedClock(TimeSpan.FromHours(3));
            Assert.Equal(400, (await Service(db, tenantId, managerId, "FacilitiesManager", lateClock)
                .TechniciansAsync(campusId, null, 1, 25, startUtc: scheduleStart,
                    endUtc: scheduleEnd, workOrderId: approved.WorkOrderId)).StatusCode);
            Assert.Equal(400, (await Service(db, tenantId, managerId, "FacilitiesManager", lateClock)
                .TransitionAsync(submitted.Id, "schedule",
                    ScheduleRetry(techId, scheduleStart, scheduleEnd, originalScheduleNote))).StatusCode);
            Assert.Empty((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "schedule"));
            var drift = await Service(db, tenantId, managerId, "FacilitiesManager",
                new StepClock(2, TimeSpan.FromHours(3))).TransitionAsync(
                    submitted.Id, "schedule",
                    ScheduleRetry(techId, scheduleStart, scheduleEnd, originalScheduleNote));
            Assert.Equal(409, drift.StatusCode);
            Assert.Empty((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "schedule"));
            Assert.Equal("Approved", (await db.FacilityWorkOrders.AsNoTracking()
                .SingleAsync(x => x.FacilityServiceRequestId == submitted.Id)).StateCode);
            var result = await service.TransitionAsync(
                submitted.Id, "schedule", ScheduleRetry(techId, scheduleStart, scheduleEnd, originalScheduleNote));
            Assert.True(result.Success, result.Message);
            scheduled = result.Data!;
            var exactRetry = await service.TransitionAsync(
                submitted.Id, "schedule", ScheduleRetry(techId, scheduleStart, scheduleEnd, originalScheduleNote));
            Assert.True(exactRetry.Success, exactRetry.Message);
            Assert.Equal(scheduled.WorkOrderId, exactRetry.Data!.WorkOrderId);
            Assert.Equal(409, (await service.TransitionAsync(
                submitted.Id, "schedule", ScheduleRetry(managerId, scheduleStart, scheduleEnd))).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(
                submitted.Id, "schedule", ScheduleRetry(techId, scheduleStart, scheduleEnd.AddMinutes(15)))).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(
                submitted.Id, "schedule", ScheduleRetry(techId, scheduleStart, scheduleEnd, "embedded"))).StatusCode);
            Assert.Single((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "schedule"));
        }
        // Availability is advisory and read-only; an authorized current order
        // may be excluded for rescheduling, but a competing assignment cannot.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            Assert.Empty((await service.TechniciansAsync(campusId, null, 1, 25,
                startUtc: scheduleStart, endUtc: scheduleEnd)).Data!.Items);
            Assert.Equal(techId, Assert.Single((await service.TechniciansAsync(
                campusId, null, 1, 25, startUtc: scheduleStart, endUtc: scheduleEnd,
                workOrderId: scheduled.WorkOrderId)).Data!.Items).UserId);
            Assert.Single((await service.TechniciansAsync(campusId, null, 1, 25,
                startUtc: scheduleEnd.AddMinutes(1), endUtc: scheduleEnd.AddHours(1))).Data!.Items);
            Assert.Equal(400, (await service.TechniciansAsync(campusId, null, 1, 25,
                startUtc: scheduleStart)).StatusCode);
            Assert.Equal(400, (await service.TechniciansAsync(campusId, null, 1, 25,
                startUtc: scheduleEnd, endUtc: scheduleStart)).StatusCode);
            Assert.Equal(403, (await service.TechniciansAsync(campusId, null, 1, 25,
                startUtc: scheduleStart, endUtc: scheduleEnd, workOrderId: long.MaxValue)).StatusCode);
            Assert.Single((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "schedule"));
        }
        // Existing schedules need a reason, current rowversion and immutable history.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var nextStart = scheduleStart.AddMinutes(5);
            var nextEnd = scheduleEnd.AddMinutes(5);
            FacilityTransitionDto Change(Guid id, string? reason, string version,
                DateTime start, DateTime end) => new()
                {
                    ClientCommandId = id, RowVersion = version, TechnicianUserId = techId,
                    ScheduledStartUtc = start, ScheduledEndUtc = end, Note = reason
                };
            var originalVersion = scheduled.WorkOrderRowVersion!;
            Assert.Equal(400, (await service.TransitionAsync(submitted.Id, "schedule",
                Change(Guid.NewGuid(), null, originalVersion, nextStart, nextEnd))).StatusCode);
            Assert.Equal(400, (await service.TransitionAsync(submitted.Id, "schedule",
                Change(Guid.NewGuid(), "short", originalVersion, nextStart, nextEnd))).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "schedule",
                Change(Guid.NewGuid(), "Nothing has changed in the window.", originalVersion,
                    scheduleStart, scheduleEnd))).StatusCode);
            Assert.Single((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "schedule"));
            var changeId = Guid.NewGuid();
            var reason = "Shifted repair window due to campus access.";
            var command = Change(changeId, reason, originalVersion, nextStart, nextEnd);
            var result = await service.TransitionAsync(submitted.Id, "schedule", command);
            Assert.True(result.Success, result.Message);
            Assert.Equal(scheduled.WorkOrderId, result.Data!.WorkOrderId);
            Assert.NotEqual(originalVersion, result.Data.WorkOrderRowVersion);
            Assert.Equal(nextStart, result.Data.ScheduledStartUtc);
            Assert.Equal(nextEnd, result.Data.ScheduledEndUtc);
            scheduled = result.Data;
            Assert.True((await service.TransitionAsync(submitted.Id, "schedule", command)).Success);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "schedule",
                Change(changeId, "Different reason must be rejected.", originalVersion,
                    nextStart, nextEnd))).StatusCode);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "schedule",
                Change(Guid.NewGuid(), "Stale version must be rejected.", originalVersion,
                    nextStart, nextEnd.AddMinutes(5)))).StatusCode);
            var events = (await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "schedule").ToList();
            Assert.Equal(2, events.Count);
            Assert.Contains(events, x => x.Note != null &&
                x.Note.Contains("Previous technician=") &&
                x.Note.Contains("reason=" + reason));
        }
        // A second approved request cannot double-book the same technician.
        FacilityRequestDto second;
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var result = await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .CreateAsync(new CreateFacilityRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), CampusId = campusId, FacilitySpaceId = spaceId,
                    CategoryCode = "ELECTRICAL", PriorityCode = "Normal", Summary = "Second hallway repair"
                });
            Assert.True(result.Success, result.Message);
            second = result.Data!;
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var result = await Service(db, tenantId, managerId, "FacilitiesManager")
                .TransitionAsync(second.Id, "triage", new FacilityTransitionDto
                {
                    ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion,
                    SlaDueAtUtc = DateTime.UtcNow.AddHours(5)
                });
            Assert.True(result.Success, result.Message);
            second = result.Data!;
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var result = await Service(db, tenantId, approverId, "FacilitiesApprover")
                .TransitionAsync(second.Id, "approve", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion });
            Assert.True(result.Success, result.Message);
            second = result.Data!;
        }
        // Two distinct work orders cannot hold overlapping approved bookings
        // for the same canonical supplier, even when both proposals are valid.
        var competingVendorStart = DateTime.UtcNow.AddMinutes(45);
        var competingVendorEnd = competingVendorStart.AddMinutes(30);
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var firstProposal = await Service(db, tenantId, managerId, "FacilitiesManager")
                .TransitionAsync(submitted.Id, "vendorpropose", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion!,
                  VendorSupplierId = approvedVendorId, VendorStartUtc = competingVendorStart,
                  VendorEndUtc = competingVendorEnd,
                  Note = "Rebook released supplier for the first work order." });
            Assert.True(firstProposal.Success, firstProposal.Message);
            scheduled = firstProposal.Data!;
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var accepted = await Service(db, tenantId, approverId, "FacilitiesApprover")
                .TransitionAsync(submitted.Id, "vendorapprove", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion!,
                  Note = "Independent first supplier booking authorization." });
            Assert.True(accepted.Success, accepted.Message);
            scheduled = accepted.Data!;
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var blockedPreview = await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: second.WorkOrderId,
                startUtc: competingVendorStart, endUtc: competingVendorEnd);
            Assert.True(blockedPreview.Success, blockedPreview.Message);
            Assert.Empty(blockedPreview.Data!.Items);
            var adjacentPreview = await service.VendorsAsync(campusId, null, 1, 25,
                workOrderId: second.WorkOrderId,
                startUtc: competingVendorEnd, endUtc: competingVendorEnd.AddMinutes(20));
            Assert.True(adjacentPreview.Success, adjacentPreview.Message);
            Assert.Single(adjacentPreview.Data!.Items);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var proposed = await Service(db, tenantId, managerId, "FacilitiesManager")
                .TransitionAsync(second.Id, "vendorpropose", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.WorkOrderRowVersion!,
                  VendorSupplierId = approvedVendorId, VendorStartUtc = competingVendorStart,
                  VendorEndUtc = competingVendorEnd,
                  Note = "Second booking must not double book approved supplier." });
            Assert.True(proposed.Success, proposed.Message);
            second = proposed.Data!;
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover");
            var denied = await service.TransitionAsync(second.Id, "vendorapprove",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = second.WorkOrderRowVersion!,
                    Note = "Overlapping vendor booking must be rejected." });
            Assert.Equal(409, denied.StatusCode);
            Assert.Equal("Proposed", (await service.ListAsync(campusId, null, 1, 25))
                .Data!.Items.Single(x => x.Id == second.Id).VendorStateCode);
            var rejected = await service.TransitionAsync(second.Id, "vendorreject",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = second.WorkOrderRowVersion!,
                    Note = "Rejected due to the existing approved supplier booking." });
            Assert.True(rejected.Success, rejected.Message);
            second = rejected.Data!;
            Assert.Equal("Rejected", second.VendorStateCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            Assert.Empty((await service.TechniciansAsync(campusId, null, 1, 25,
                startUtc: scheduleStart, endUtc: scheduleEnd,
                workOrderId: second.WorkOrderId)).Data!.Items);
            var result = await service.TransitionAsync(second.Id, "schedule", new FacilityTransitionDto
            {
                ClientCommandId = Guid.NewGuid(), RowVersion = second.WorkOrderRowVersion!,
                TechnicianUserId = techId, ScheduledStartUtc = DateTime.UtcNow.AddMinutes(30),
                ScheduledEndUtc = DateTime.UtcNow.AddHours(1)
            });
            Assert.Equal(409, result.StatusCode);
            var rows = await service.ListAsync(campusId, null, 1, 25);
            Assert.Equal("Approved", rows.Data!.Items.Single(x => x.Id == second.Id).WorkOrderState);
        }
        // An unassigned approved work order has an independent, reasoned cancellation
        // decision. It cannot silently bypass technician scheduling or become Closed.
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var denied = await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .TransitionAsync(second.Id, "cancelrequest", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.WorkOrderRowVersion!,
                  Note = "Requester must not cancel their own approved order." });
            Assert.Equal(403, denied.StatusCode);
        }
        FacilityRequestDto cancellationPending;
        var originalCancelCommandId = Guid.NewGuid();
        var originalCancelRowVersion = second.WorkOrderRowVersion!;
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var commandId = originalCancelCommandId;
            var proposed = await service.TransitionAsync(second.Id, "cancelrequest",
                new FacilityTransitionDto { ClientCommandId = commandId,
                    RowVersion = second.WorkOrderRowVersion!,
                    Note = "Duplicate repair request; no technician has been assigned." });
            Assert.True(proposed.Success, proposed.Message);
            cancellationPending = proposed.Data!;
            Assert.Equal("CancellationPending", cancellationPending.WorkOrderState);
            var replay = await service.TransitionAsync(second.Id, "cancelrequest",
                new FacilityTransitionDto { ClientCommandId = commandId,
                    RowVersion = second.WorkOrderRowVersion!,
                    Note = "Duplicate repair request; no technician has been assigned." });
            Assert.True(replay.Success, replay.Message);
            var altered = await service.TransitionAsync(second.Id, "cancelrequest",
                new FacilityTransitionDto { ClientCommandId = commandId,
                    RowVersion = second.WorkOrderRowVersion!,
                    Note = "Different cancellation reason must be rejected." });
            Assert.Equal(409, altered.StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "TenantAdmin"))
        {
            var makerCannotCheck = await Service(db, tenantId, managerId, "TenantAdmin")
                .TransitionAsync(second.Id, "cancelapprove", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = cancellationPending.WorkOrderRowVersion!,
                  Note = "The cancellation maker cannot approve their own decision." });
            Assert.Equal(403, makerCannotCheck.StatusCode);
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover");
            var rejected = await service.TransitionAsync(second.Id, "cancelreject",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = cancellationPending.WorkOrderRowVersion!,
                    Note = "Keep this approved repair request available for reassignment." });
            Assert.True(rejected.Success, rejected.Message);
            Assert.Equal("Approved", rejected.Data!.WorkOrderState);
            second = rejected.Data;
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var obsoleteCancellation = await service.TransitionAsync(second.Id, "cancelrequest",
                new FacilityTransitionDto
                {
                    ClientCommandId = originalCancelCommandId,
                    RowVersion = originalCancelRowVersion,
                    Note = "Duplicate repair request; no technician has been assigned."
                });
            Assert.Equal(409, obsoleteCancellation.StatusCode);
            Assert.Single((await service.HistoryAsync(second.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "cancelrequest"));
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var proposed = await Service(db, tenantId, managerId, "FacilitiesManager")
                .TransitionAsync(second.Id, "cancelrequest", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.WorkOrderRowVersion!,
                  Note = "Campus confirmed that the second repair is unnecessary." });
            Assert.True(proposed.Success, proposed.Message);
            cancellationPending = proposed.Data!;
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover");
            var approvedCancel = await service.TransitionAsync(second.Id, "cancelapprove",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = cancellationPending.WorkOrderRowVersion!,
                    Note = "Verified duplicate service request; cancellation is authorized." });
            Assert.True(approvedCancel.Success, approvedCancel.Message);
            Assert.Equal("Cancelled", approvedCancel.Data!.WorkOrderState);
            Assert.Null(approvedCancel.Data.VerifiedAtUtc);
            Assert.Equal(1, (await service.DashboardAsync(campusId)).Data!.Cancelled);
            var stale = await service.TransitionAsync(second.Id, "cancelreject",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = cancellationPending.WorkOrderRowVersion!,
                    Note = "Stale cancellation decision must not restore the work order." });
            Assert.Equal(409, stale.StatusCode);
            var overdue = await Service(db, tenantId, approverId, "FacilitiesApprover",
                new ShiftedClock(TimeSpan.FromHours(6)))
                .ListAsync(campusId, null, 1, 25, default, true);
            Assert.DoesNotContain(overdue.Data!.Items, x => x.Id == second.Id);
            var history = (await service.HistoryAsync(second.Id, 1, 25)).Data!;
            Assert.Equal(4, history.Items.Count(x => x.ActionCode.StartsWith("cancel")));
            Assert.Equal(1, await db.FacilityWorkOrders.CountAsync(x =>
                x.TenantId == tenantId && x.FacilityServiceRequestId == second.Id));
        }
        // Manager releases a not-started schedule with an immutable snapshot; stale and
        // unauthorized attempts cannot silently remove another worker's assignment.
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var forbidden = await Service(db, tenantId, techId, "FacilitiesTechnician")
                .TransitionAsync(submitted.Id, "unschedule", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion!,
                  Note = "Technician may not release assignment." });
            Assert.Equal(403, forbidden.StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var releaseId = Guid.NewGuid();
            var released = await service.TransitionAsync(submitted.Id, "unschedule", new FacilityTransitionDto
            { ClientCommandId = releaseId, RowVersion = scheduled.WorkOrderRowVersion!,
              Note = "Schedule changed; reason=embedded" });
            Assert.True(released.Success, released.Message);
            Assert.Equal("Approved", released.Data!.WorkOrderState);
            Assert.Null(released.Data.TechnicianUserId);
            Assert.Null(released.Data.ScheduledStartUtc);
            Assert.Null(released.Data.ScheduledEndUtc);
            var retry = await service.TransitionAsync(submitted.Id, "unschedule", new FacilityTransitionDto
            { ClientCommandId = releaseId, RowVersion = scheduled.WorkOrderRowVersion!,
              Note = "Schedule changed; reason=embedded" });
            Assert.True(retry.Success, retry.Message);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "unschedule",
                new FacilityTransitionDto { ClientCommandId = releaseId,
                    RowVersion = scheduled.WorkOrderRowVersion!,
                    Note = "embedded" })).StatusCode);
            var stale = await service.TransitionAsync(submitted.Id, "unschedule", new FacilityTransitionDto
            { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion!,
              Note = "Attempt to release stale schedule." });
            Assert.Equal(409, stale.StatusCode);
            var history = await service.HistoryAsync(submitted.Id, 1, 25);
            Assert.Contains(history.Data!.Items, x => x.ActionCode == "unschedule" &&
                x.Note!.Contains("Released technician=") && x.Note.Contains("reason="));
            var rescheduled = await service.TransitionAsync(submitted.Id, "schedule", new FacilityTransitionDto
            { ClientCommandId = Guid.NewGuid(), RowVersion = released.Data.WorkOrderRowVersion!,
              TechnicianUserId = techId, ScheduledStartUtc = DateTime.UtcNow.AddMinutes(15),
              ScheduledEndUtc = DateTime.UtcNow.AddHours(2) });
            Assert.True(rescheduled.Success, rescheduled.Message);
            scheduled = rescheduled.Data!;
            // The original schedule was released and replaced. An old exact
            // command must not claim success for the newer assignment.
            var obsoleteSchedule = await service.TransitionAsync(submitted.Id, "schedule",
                new FacilityTransitionDto
                {
                    ClientCommandId = scheduleCommandId,
                    RowVersion = approved.WorkOrderRowVersion!,
                    TechnicianUserId = techId, ScheduledStartUtc = scheduleStart,
                    ScheduledEndUtc = scheduleEnd,
                    Note = "Campus reschedule; reason=embedded"
                });
            Assert.Equal(409, obsoleteSchedule.StatusCode);
            Assert.Equal(3, (await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Count(x => x.ActionCode == "schedule"));
        }
        // Advance a test clock, not stored SLA: queue, role denial, retry and concurrency.
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var denied = await Service(db, tenantId, requesterId, "FacilitiesRequester",
                new ShiftedClock(TimeSpan.FromHours(6))).TransitionAsync(submitted.Id, "escalate",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = scheduled.WorkOrderRowVersion!,
                    Note = "Requester cannot escalate their own work order." });
            Assert.Equal(403, denied.StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromHours(6)));
            var queue = await service.ListAsync(campusId, null, 1, 25, default, true);
            Assert.Contains(queue.Data!.Items, x => x.Id == submitted.Id);
            Assert.DoesNotContain(queue.Data.Items, x => x.Id == privateRequestId);
            var commandId = Guid.NewGuid();
            var escalated = await service.TransitionAsync(submitted.Id, "escalate",
                new FacilityTransitionDto { ClientCommandId = commandId,
                    RowVersion = scheduled.WorkOrderRowVersion!,
                    Note = "Overdue hallway repair requires immediate manager follow-up." });
            Assert.True(escalated.Success, escalated.Message);
            Assert.Equal("Scheduled", escalated.Data!.WorkOrderState);
            Assert.NotEqual(scheduled.WorkOrderRowVersion, escalated.Data.WorkOrderRowVersion);
            var retry = await service.TransitionAsync(submitted.Id, "escalate",
                new FacilityTransitionDto { ClientCommandId = commandId,
                    RowVersion = scheduled.WorkOrderRowVersion!,
                    Note = "Overdue hallway repair requires immediate manager follow-up." });
            Assert.True(retry.Success, retry.Message);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "escalate",
                new FacilityTransitionDto { ClientCommandId = commandId,
                    RowVersion = scheduled.WorkOrderRowVersion!,
                    Note = "Changed escalation reason must not be acknowledged." })).StatusCode);
            var stale = await service.TransitionAsync(submitted.Id, "escalate",
                new FacilityTransitionDto { ClientCommandId = Guid.NewGuid(),
                    RowVersion = scheduled.WorkOrderRowVersion!,
                    Note = "Attempt to escalate a stale work order version." });
            Assert.Equal(409, stale.StatusCode);
            Assert.Contains((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items,
                x => x.ActionCode == "escalate" && x.FromState == x.ToState &&
                     x.Note!.Contains("Manual overdue SLA escalation"));
            scheduled = escalated.Data;
        }
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var service = Service(db, tenantId, techId, "FacilitiesTechnician");
            var visible = await service.ListAsync(null, null, 1, 25);
            Assert.Single(visible.Data!.Items);
            Assert.Equal(submitted.Id, visible.Data.Items[0].Id);
            Assert.Equal(404, (await service.HistoryAsync(privateRequestId, 1, 25)).StatusCode);
        }
        // A supplier revoked after booking approval cannot be used to start
        // work. Failed attempts must leave state and immutable audit unchanged.
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            await db.ProcurementSuppliers.Where(x => x.Id == approvedVendorId)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.StateCode, "Inactive"));
        }
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var service = Service(db, tenantId, techId, "FacilitiesTechnician");
            var denied = await service.TransitionAsync(submitted.Id, "start",
                new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion! });
            Assert.Equal(409, denied.StatusCode);
            Assert.Equal("Scheduled", (await service.ListAsync(campusId, null, 1, 25))
                .Data!.Items.Single(x => x.Id == submitted.Id).WorkOrderState);
            Assert.Equal(0, await db.Set<FacilityWorkflowEvent>().CountAsync(x =>
                x.FacilityServiceRequestId == submitted.Id && x.ActionCode == "start"));
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            await db.ProcurementSuppliers.Where(x => x.Id == approvedVendorId)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.StateCode, "Approved"));
        }
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            // Vendor booking ends before the scheduled technician window.
            var service = Service(db, tenantId, techId, "FacilitiesTechnician",
                new ShiftedClock(TimeSpan.FromMinutes(90)));
            var expired = await service.TransitionAsync(submitted.Id, "start",
                new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion! });
            Assert.Equal(409, expired.StatusCode);
            Assert.Equal(0, await db.Set<FacilityWorkflowEvent>().CountAsync(x =>
                x.FacilityServiceRequestId == submitted.Id && x.ActionCode == "start"));
        }
        // The first three clock reads pass; the pre-commit fourth read expires.
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var drift = await Service(db, tenantId, techId, "FacilitiesTechnician",
                new StepClock(4, TimeSpan.FromHours(5)))
                .TransitionAsync(submitted.Id, "start", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = scheduled.WorkOrderRowVersion! });
            Assert.Equal(409, drift.StatusCode);
            Assert.Equal("Scheduled", (await db.FacilityWorkOrders.AsNoTracking()
                .SingleAsync(x => x.FacilityServiceRequestId == submitted.Id)).StateCode);
            Assert.Equal(0, await db.FacilityWorkflowEvents.CountAsync(x =>
                x.FacilityServiceRequestId == submitted.Id && x.ActionCode == "start"));
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
            var service = Service(db, tenantId, techId, "FacilitiesTechnician");
            var completionId = Guid.NewGuid();
            FacilityTransitionDto Completion(string evidence) => new()
            {
                ClientCommandId = completionId, RowVersion = started.WorkOrderRowVersion!,
                Note = evidence
            };
            var result = await service.TransitionAsync(submitted.Id, "complete",
                Completion("Replaced the hallway light and verified the circuit."));
            Assert.True(result.Success, result.Message);
            completed = result.Data!;
            Assert.Null(completed.SlaBreached);
            Assert.True(completed.CompletedAtUtc < completed.SlaDueAtUtc);
            Assert.True((await service.TransitionAsync(submitted.Id, "complete",
                Completion("Replaced the hallway light and verified the circuit."))).Success);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "complete",
                Completion("Changed completion evidence must not be accepted."))).StatusCode);
            Assert.Single((await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
                .Where(x => x.ActionCode == "complete"));
        }
        // Independent verifier may return unsafe completion for correction without
        // falsifying the prior evidence or marking an unfinished job as complete.
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            var denied = await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .TransitionAsync(submitted.Id, "return", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = completed.WorkOrderRowVersion!,
                  Note = "Requester must not return their own completion." });
            Assert.Equal(403, denied.StatusCode);
        }
        var returnCommandId = Guid.NewGuid();
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover");
            var returned = await service.TransitionAsync(submitted.Id, "return", new FacilityTransitionDto
            { ClientCommandId = returnCommandId, RowVersion = completed.WorkOrderRowVersion!,
              Note = "Inspection found unsafe wiring; replace the connector." });
            Assert.True(returned.Success, returned.Message);
            Assert.Equal("InProgress", returned.Data!.WorkOrderState);
            Assert.Null(returned.Data.CompletedAtUtc);
            Assert.Null(returned.Data.CompletionEvidence);
            var replay = await service.TransitionAsync(submitted.Id, "return", new FacilityTransitionDto
            { ClientCommandId = returnCommandId, RowVersion = completed.WorkOrderRowVersion!,
              Note = "Inspection found unsafe wiring; replace the connector." });
            Assert.True(replay.Success, replay.Message);
            var alteredReplay = await service.TransitionAsync(submitted.Id, "return", new FacilityTransitionDto
            { ClientCommandId = returnCommandId, RowVersion = completed.WorkOrderRowVersion!,
              Note = "Different return reason should be rejected." });
            Assert.Equal(409, alteredReplay.StatusCode);
            var staleVerify = await service.TransitionAsync(submitted.Id, "verify", new FacilityTransitionDto
            { ClientCommandId = Guid.NewGuid(), RowVersion = completed.WorkOrderRowVersion!,
              Note = "Stale verifier must not close the returned job." });
            Assert.Equal(409, staleVerify.StatusCode);
            var events = (await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items;
            Assert.Contains(events, x => x.ActionCode == "complete" &&
                x.Note!.Contains("Replaced the hallway light"));
            Assert.Single(events.Where(x => x.ActionCode == "return"));
            completed = returned.Data;
        }
        await using (var db = Db(options, tenantId, techId, "FacilitiesTechnician"))
        {
            var result = await Service(db, tenantId, techId, "FacilitiesTechnician")
                .TransitionAsync(submitted.Id, "complete", new FacilityTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = completed.WorkOrderRowVersion!,
                  Note = "Replaced unsafe connector and retested the hallway circuit." });
            Assert.True(result.Success, result.Message);
            completed = result.Data!;
        }
        await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
        {
            var service = Service(db, tenantId, approverId, "FacilitiesApprover",
                new ShiftedClock(TimeSpan.FromHours(6)));
            // The technician finished before SLA, but independent closure is late.
            // PendingVerification remains overdue until the verifier closes it.
            var pendingOverdue = await service.ListAsync(campusId, null, 1, 25, default, true);
            Assert.Contains(pendingOverdue.Data!.Items, x => x.Id == submitted.Id);
            var verificationId = Guid.NewGuid();
            FacilityTransitionDto Verification(string reason) => new()
            {
                ClientCommandId = verificationId, RowVersion = completed.WorkOrderRowVersion!,
                Note = reason
            };
            var result = await service.TransitionAsync(submitted.Id, "verify",
                Verification("Inspected working light on site."));
            Assert.True(result.Success, result.Message);
            Assert.True((await service.TransitionAsync(submitted.Id, "verify",
                Verification("Inspected working light on site."))).Success);
            Assert.Equal(409, (await service.TransitionAsync(submitted.Id, "verify",
                Verification("Different verification outcome."))).StatusCode);
            Assert.Equal("Closed", result.Data!.WorkOrderState);
            Assert.NotNull(result.Data.VerifiedAtUtc);
            Assert.True(result.Data.CompletedAtUtc < result.Data.SlaDueAtUtc);
            Assert.True(result.Data.VerifiedAtUtc > result.Data.SlaDueAtUtc);
            Assert.Equal<bool?>(true, result.Data.SlaBreached);
            var dashboard = await service.DashboardAsync(campusId);
            Assert.Equal(1, dashboard.Data!.ClosedLate);
            var history = await service.HistoryAsync(submitted.Id, 1, 25);
            Assert.Equal(18, history.Data!.TotalCount); // Includes five audited vendor decisions.
            var overdue = await Service(db, tenantId, approverId, "FacilitiesApprover",
                new ShiftedClock(TimeSpan.FromHours(6)))
                .ListAsync(campusId, null, 1, 25, default, true);
            Assert.DoesNotContain(overdue.Data!.Items, x => x.Id == submitted.Id);
            Assert.Equal("verify", history.Data.Items.Last().ActionCode);
            Assert.Single(await db.FacilityWorkOrders.Where(x => x.FacilityServiceRequestId == submitted.Id).ToListAsync());
        }
        // A completed/verified work order cannot be used as an active vendor preview.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            Assert.Equal(403, (await Service(db, tenantId, managerId, "FacilitiesManager")
                .VendorsAsync(campusId, null, 1, 25, workOrderId: approved.WorkOrderId)).StatusCode);
        }
        await using (var db = Db(options, otherTenantId, requesterId, "FacilitiesRequester"))
        {
            var service = Service(db, otherTenantId, requesterId, "FacilitiesRequester");
            Assert.Empty((await service.ListAsync(null, null, 1, 25)).Data!.Items);
            Assert.Equal(404, (await service.HistoryAsync(submitted.Id, 1, 25)).StatusCode);
        }

        // Concurrent retries from independent request contexts must converge on one
        // durable row and Create event, with payload mismatch still rejected.
        var parallelId = Guid.NewGuid();
        async Task<FacilityRequestDto> SubmitParallelAsync()
        {
            await using var parallelDb = Db(options, tenantId, requesterId, "FacilitiesRequester");
            var result = await Service(parallelDb, tenantId, requesterId, "FacilitiesRequester")
                .CreateAsync(new CreateFacilityRequestDto
                {
                    ClientRequestId = parallelId, CampusId = campusId, FacilitySpaceId = spaceId,
                    CategoryCode = "ELECTRICAL", PriorityCode = "Normal",
                    Summary = "Concurrent hallway repair request"
                });
            Assert.True(result.Success, result.Message);
            return result.Data!;
        }
        var parallel = await Task.WhenAll(SubmitParallelAsync(), SubmitParallelAsync());
        Assert.Equal(parallel[0].Id, parallel[1].Id);
        await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
        {
            Assert.Equal(1, await db.Set<FacilityServiceRequest>().CountAsync(x =>
                x.TenantId == tenantId && x.ClientRequestId == parallelId));
            Assert.Equal(1, await db.Set<FacilityWorkflowEvent>().CountAsync(x =>
                x.TenantId == tenantId && x.ClientCommandId == parallelId && x.ActionCode == "Create"));
            var mismatch = await Service(db, tenantId, requesterId, "FacilitiesRequester")
                .CreateAsync(new CreateFacilityRequestDto
                {
                    ClientRequestId = parallelId, CampusId = campusId, FacilitySpaceId = spaceId,
                    CategoryCode = "ELECTRICAL", PriorityCode = "Normal",
                    Summary = "Different payload with same command ID"
                });
            Assert.Equal(409, mismatch.StatusCode);
        }

        // Independent SQL connections race to schedule two different approved work orders
        // for the SAME technician and UTC window. The transaction-owned application lock
        // must admit only one; a losing request must not leave an assignment or audit event.
        var competingOrders = new List<FacilityRequestDto>();
        for (var i = 0; i < 2; i++)
        {
            FacilityRequestDto candidate;
            await using (var db = Db(options, tenantId, requesterId, "FacilitiesRequester"))
            {
                var created = await Service(db, tenantId, requesterId, "FacilitiesRequester")
                    .CreateAsync(new CreateFacilityRequestDto
                    {
                        ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                        FacilitySpaceId = spaceId, CategoryCode = "ELECTRICAL",
                        PriorityCode = "Normal", Summary = "Parallel technician repair " + i
                    });
                Assert.True(created.Success, created.Message);
                candidate = created.Data!;
            }
            await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            {
                var parallelTriage = await Service(db, tenantId, managerId, "FacilitiesManager")
                    .TransitionAsync(candidate.Id, "triage", new FacilityTransitionDto
                    {
                        ClientCommandId = Guid.NewGuid(), RowVersion = candidate.RowVersion,
                        SlaDueAtUtc = DateTime.UtcNow.AddHours(12)
                    });
                Assert.True(parallelTriage.Success, parallelTriage.Message);
                candidate = parallelTriage.Data!;
            }
            await using (var db = Db(options, tenantId, approverId, "FacilitiesApprover"))
            {
                var approvedParallel = await Service(db, tenantId, approverId, "FacilitiesApprover")
                    .TransitionAsync(candidate.Id, "approve", new FacilityTransitionDto
                    {
                        ClientCommandId = Guid.NewGuid(), RowVersion = candidate.RowVersion
                    });
                Assert.True(approvedParallel.Success, approvedParallel.Message);
                candidate = approvedParallel.Data!;
            }
            competingOrders.Add(candidate);
        }
        var startParallel = DateTime.UtcNow.AddHours(8);
        var endParallel = startParallel.AddHours(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(bool Success, int Status)> ScheduleCompetingAsync(FacilityRequestDto candidate)
        {
            await using var db = Db(options, tenantId, managerId, "FacilitiesManager");
            await gate.Task;
            var result = await Service(db, tenantId, managerId, "FacilitiesManager")
                .TransitionAsync(candidate.Id, "schedule", new FacilityTransitionDto
                {
                    ClientCommandId = Guid.NewGuid(), RowVersion = candidate.WorkOrderRowVersion!,
                    TechnicianUserId = techId, ScheduledStartUtc = startParallel,
                    ScheduledEndUtc = endParallel
                });
            return (result.Success, result.StatusCode);
        }
        var contenders = competingOrders.Select(ScheduleCompetingAsync).ToArray();
        gate.SetResult();
        var outcomes = await Task.WhenAll(contenders);
        Assert.Single(outcomes.Where(x => x.Success));
        Assert.Single(outcomes.Where(x => !x.Success && x.Status == 409));
        var competingIds = competingOrders.Select(x => x.Id).ToArray();
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            Assert.Equal(1, await db.Set<FacilityWorkOrder>().CountAsync(x =>
                x.TenantId == tenantId && competingIds.Contains(x.FacilityServiceRequestId) &&
                x.StateCode == "Scheduled" && x.AssignedTechnicianUserId == techId));
            Assert.Equal(1, await db.Set<FacilityWorkflowEvent>().CountAsync(x =>
                x.TenantId == tenantId && competingIds.Contains(x.FacilityServiceRequestId) &&
                x.ActionCode == "schedule"));
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

    private static FacilityWorkflowService Service(EduOSDbContext db, long tenantId, long userId,
        string role, TimeProvider? time = null) =>
        new(db, new Principal(tenantId, userId, role), time ?? TimeProvider.System,
            NullLogger<FacilityWorkflowService>.Instance);

    private sealed class ShiftedClock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }

    // Deterministic clock drift inside a single authoritative command.
    private sealed class StepClock(int jumpAtRead, TimeSpan afterJump) : TimeProvider
    {
        private int reads;
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.UtcNow.Add(
                global::System.Threading.Interlocked.Increment(ref reads) >= jumpAtRead
                    ? afterJump : TimeSpan.Zero);
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