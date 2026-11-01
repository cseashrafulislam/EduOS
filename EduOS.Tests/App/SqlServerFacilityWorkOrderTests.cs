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
                // A campus-granted active member without the technician role must
                // not be assignable, even by a Facilities manager.
                setup.TenantMemberships.AddRange(
                    new TenantMembership
                    { TenantId = tenantId, UserId = techId, Status = MembershipStatus.Active },
                    new TenantMembership
                    { TenantId = tenantId, UserId = managerId, Status = MembershipStatus.Active });
                var technicianRole = new ApplicationRole
                {
                    TenantId = tenantId, Name = "FacilitiesTechnician",
                    NormalizedName = "FACILITIESTECHNICIAN", IsActive = true
                };
                setup.Roles.Add(technicianRole);
                await setup.SaveChangesAsync();
                setup.UserRoles.Add(new IdentityUserRole<long>
                { UserId = techId, RoleId = technicianRole.Id });
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
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
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
            Assert.Equal(2, (await service.HistoryAsync(submitted.Id, 1, 25)).Data!.Items
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
            Assert.Equal(12, history.Data!.TotalCount);
            var overdue = await Service(db, tenantId, approverId, "FacilitiesApprover",
                new ShiftedClock(TimeSpan.FromHours(6)))
                .ListAsync(campusId, null, 1, 25, default, true);
            Assert.DoesNotContain(overdue.Data!.Items, x => x.Id == submitted.Id);
            Assert.Equal("verify", history.Data.Items.Last().ActionCode);
            Assert.Single(await db.FacilityWorkOrders.Where(x => x.FacilityServiceRequestId == submitted.Id).ToListAsync());
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