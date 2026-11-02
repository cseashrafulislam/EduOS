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
                var managerRole = new ApplicationRole
                { TenantId = tenantId, Name = "FacilitiesManager", NormalizedName = "FACILITIESMANAGER", IsActive = true };
                var hostRole = new ApplicationRole
                { TenantId = tenantId, Name = "Staff", NormalizedName = "STAFF", IsActive = true };
                setup.Roles.AddRange(managerRole, hostRole);
                await setup.SaveChangesAsync();
                setup.UserRoles.AddRange(
                    new IdentityUserRole<long> { UserId = managerId, RoleId = managerRole.Id },
                    new IdentityUserRole<long> { UserId = hostId, RoleId = hostRole.Id });
                await setup.SaveChangesAsync();
            }
        }
        var start = DateTime.UtcNow.AddHours(1);
        var end = start.AddHours(2);
        Guid firstCommandId = Guid.Empty;
        async Task<FacilityVisitorDto> PreregisterAsync(string visitor)
        {
            await using var db = Db(options, tenantId, managerId, "FacilitiesManager");
            var commandId = Guid.NewGuid();
            if (visitor == "Guest One") firstCommandId = commandId;
            var result = await Service(db, tenantId, managerId, "FacilitiesManager")
                .CreateAsync(new CreateFacilityVisitorDto
                {
                    ClientRequestId = commandId, CampusId = campusId, HostUserId = hostId,
                    VisitorName = visitor, Purpose = "Scheduled campus meeting",
                    VisitStartUtc = start, VisitEndUtc = end
                });
            Assert.True(result.Success, result.Message);
            return result.Data!;
        }
        var first = await PreregisterAsync("Guest One");
        var second = await PreregisterAsync("Guest Two");
        // New appointments must obey campus duration policy; rejected writes leave no event.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var duration = new TenantSetting
            {
                TenantId = tenantId, Key = "Facilities.Visitor.MaxVisitMinutes." + campusId,
                Value = "90", Category = "Facilities", CreatedAt = DateTime.UtcNow,
                CreatedBy = managerId
            };
            db.TenantSettings.Add(duration);
            await db.SaveChangesAsync();
            var blocked = await Service(db, tenantId, managerId, "FacilitiesManager")
                .CreateAsync(new CreateFacilityVisitorDto
                {
                    ClientRequestId = Guid.NewGuid(), CampusId = campusId, HostUserId = hostId,
                    VisitorName = "Long Appointment", Purpose = "Extended campus meeting",
                    VisitStartUtc = start, VisitEndUtc = end
                });
            Assert.Equal(409, blocked.StatusCode);
            Assert.Equal(2, await db.Set<FacilityVisitorVisit>().CountAsync(v => v.CampusId == campusId));
            Assert.Equal(2, await db.Set<FacilityVisitorEvent>().CountAsync(e => e.ActionCode == "preregister"));
            duration.Value = "180";
            await db.SaveChangesAsync();
        }
        // A late retry of the same immutable preregistration is acknowledged,
        // but a NEW preregistration with an elapsed window is never accepted.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromHours(2)));
            var retry = await service.CreateAsync(new CreateFacilityVisitorDto
            {
                ClientRequestId = firstCommandId, CampusId = campusId, HostUserId = hostId,
                VisitorName = "Guest One", Purpose = "Scheduled campus meeting",
                VisitStartUtc = start, VisitEndUtc = end
            });
            Assert.True(retry.Success, retry.Message);
            Assert.Equal(first.Id, retry.Data!.Id);
            var lateNew = await service.CreateAsync(new CreateFacilityVisitorDto
            {
                ClientRequestId = Guid.NewGuid(), CampusId = campusId, HostUserId = hostId,
                VisitorName = "Guest Three", Purpose = "Scheduled campus meeting",
                VisitStartUtc = start, VisitEndUtc = end
            });
            Assert.Equal(409, lateNew.StatusCode);
            Assert.Equal(2, await db.Set<FacilityVisitorVisit>().CountAsync(x => x.CampusId == campusId));
            Assert.Equal(2, await db.Set<FacilityVisitorEvent>().CountAsync(x => x.ActionCode == "preregister"));
        }
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
        // Concurrent administrator revocation commits while an old cookie attempts a PII read.
        await using (var writer = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            await using var revocation = await writer.Database.BeginTransactionAsync();
            Assert.Equal(1, await writer.Roles.IgnoreQueryFilters().Where(x =>
                x.TenantId == tenantId && x.NormalizedName == "FACILITIESMANAGER")
                .ExecuteUpdateAsync(x => x.SetProperty(role => role.IsActive, false)));
            var concurrentRead = Task.Run(async () =>
            {
                await using var reader = Db(options, tenantId, managerId, "FacilitiesManager");
                return await Service(reader, tenantId, managerId, "FacilitiesManager")
                    .ListAsync(campusId, 1, 25);
            });
            await Task.Delay(150);
            await revocation.CommitAsync();
            var denied = await concurrentRead.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(403, denied.StatusCode);
            Assert.Null(denied.Data);
        }
        // A stale FacilitiesManager claim cannot discover hosts after its DB role is revoked.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.Roles.IgnoreQueryFilters().Where(x =>
                x.TenantId == tenantId && x.NormalizedName == "FACILITIESMANAGER")
                .ExecuteUpdateAsync(x => x.SetProperty(role => role.IsActive, false)));
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(403, (await Service(db, tenantId, managerId, "FacilitiesManager")
                .HostsAsync(campusId, null, 1, 25)).StatusCode);
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var revoked = Service(db, tenantId, managerId, "FacilitiesManager");
            Assert.Equal(403, (await revoked.ListAsync(campusId, 1, 25)).StatusCode);
            // A revoked manager role is an explicit 403 authorization denial.
            Assert.Equal(403, (await revoked.HistoryAsync(first.Id, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.Roles.IgnoreQueryFilters().Where(x =>
                x.TenantId == tenantId && x.NormalizedName == "FACILITIESMANAGER")
                .ExecuteUpdateAsync(x => x.SetProperty(role => role.IsActive, true)));
        // Host approval is also rejected after the live Staff role is revoked.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.Roles.IgnoreQueryFilters().Where(x =>
                x.TenantId == tenantId && x.NormalizedName == "STAFF")
                .ExecuteUpdateAsync(x => x.SetProperty(role => role.IsActive, false)));
        await using (var db = Db(options, tenantId, hostId, "Staff"))
        {
            var revoked = Service(db, tenantId, hostId, "Staff");
            Assert.Equal(403, (await revoked.TransitionAsync(first.Id, "hostapprove",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion })).StatusCode);
            Assert.Equal(403, (await revoked.ListAsync(campusId, 1, 25)).StatusCode);
            Assert.Equal(403, (await revoked.HistoryAsync(first.Id, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.Roles.IgnoreQueryFilters().Where(x =>
                x.TenantId == tenantId && x.NormalizedName == "STAFF")
                .ExecuteUpdateAsync(x => x.SetProperty(role => role.IsActive, true)));


        // Host campus and membership revocation must not append an approval
        // event even if the browser retains an old Staff claim and rowversion.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.UserCampusAccesses.Where(g =>
                g.TenantId == tenantId && g.CampusId == campusId && g.UserId == hostId)
                .ExecuteUpdateAsync(x => x.SetProperty(g => g.IsActive, false)));
        await using (var db = Db(options, tenantId, hostId, "Staff"))
        {
            var revoked = Service(db, tenantId, hostId, "Staff");
            Assert.Equal(403, (await revoked.TransitionAsync(first.Id, "hostapprove",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion })).StatusCode);
            Assert.Equal(403, (await revoked.ListAsync(campusId, 1, 25)).StatusCode);
            // Revoked campus scope masks this formerly owned visit as not found.
            Assert.Equal(404, (await revoked.HistoryAsync(first.Id, 1, 25)).StatusCode);
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.UserCampusAccesses.Where(g =>
                g.TenantId == tenantId && g.CampusId == campusId && g.UserId == hostId)
                .ExecuteUpdateAsync(x => x.SetProperty(g => g.IsActive, true)));
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.TenantMemberships.Where(m =>
                m.TenantId == tenantId && m.UserId == hostId)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.IsDeleted, true)));
        await using (var db = Db(options, tenantId, hostId, "Staff"))
            Assert.Equal(403, (await Service(db, tenantId, hostId, "Staff")
                .TransitionAsync(first.Id, "hostapprove", new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion })).StatusCode);
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.TenantMemberships.IgnoreQueryFilters().Where(m =>
                m.TenantId == tenantId && m.UserId == hostId)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.IsDeleted, false)));
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(0, await db.Set<FacilityVisitorEvent>().CountAsync(e => e.ActionCode == "hostapprove"));

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

        // A revoked desk campus grant denies check-in without changing the
        // approved visit, badge or append-only event count.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.UserCampusAccesses.Where(g =>
                g.TenantId == tenantId && g.CampusId == campusId && g.UserId == managerId)
                .ExecuteUpdateAsync(x => x.SetProperty(g => g.IsActive, false)));
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(403, (await Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromMinutes(50))).TransitionAsync(first.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion,
                    BadgeCode = "GUEST-01" })).StatusCode);
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(1, await db.UserCampusAccesses.Where(g =>
                g.TenantId == tenantId && g.CampusId == campusId && g.UserId == managerId)
                .ExecuteUpdateAsync(x => x.SetProperty(g => g.IsActive, true)));
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
            Assert.Equal(0, await db.Set<FacilityVisitorEvent>().CountAsync(e => e.ActionCode == "checkin"));
        var commandId = Guid.NewGuid();
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromMinutes(50)));
            var early = new TenantSetting
            {
                TenantId = tenantId, Key = "Facilities.Visitor.EarlyCheckInMinutes." + campusId,
                Value = "5", Category = "Facilities", CreatedAt = DateTime.UtcNow,
                CreatedBy = managerId
            };
            db.TenantSettings.Add(early);
            await db.SaveChangesAsync();
            var tooEarly = await service.TransitionAsync(first.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion, BadgeCode = "GUEST-01" });
            Assert.Equal(409, tooEarly.StatusCode);
            Assert.Equal(0, await db.Set<FacilityVisitorEvent>().CountAsync(e => e.ActionCode == "checkin"));
            early.Value = "15";
            await db.SaveChangesAsync();
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
            var incidentId = Guid.NewGuid();
            const string narrative = "Observed unauthorized badge transfer.";
            var incident = await service.TransitionAsync(first.Id, "incident",
                new FacilityVisitorTransitionDto
                { ClientCommandId = incidentId, RowVersion = first.RowVersion, Reason = narrative });
            Assert.True(incident.Success, incident.Message);
            first = incident.Data!;
            var replayIncident = await service.TransitionAsync(first.Id, "incident",
                new FacilityVisitorTransitionDto
                { ClientCommandId = incidentId, RowVersion = "stale", Reason = narrative });
            Assert.True(replayIncident.Success, replayIncident.Message);
            var alteredIncident = await service.TransitionAsync(first.Id, "incident",
                new FacilityVisitorTransitionDto
                { ClientCommandId = incidentId, RowVersion = first.RowVersion, Reason = "Different incident narrative." });
            Assert.Equal(409, alteredIncident.StatusCode);
            var history = await service.HistoryAsync(first.Id, 1, 25);
            Assert.Contains(history.Data!.Items, e => e.ActionCode == "incident" && e.Reason == narrative);
            Assert.Equal(1, await db.Set<FacilityVisitorEvent>().CountAsync(e =>
                e.FacilityVisitorVisitId == first.Id && e.ActionCode == "incident"));
        }
        await using (var db = Db(options, tenantId, hostId, "Staff"))
        {
            var history = await Service(db, tenantId, hostId, "Staff").HistoryAsync(first.Id, 1, 25);
            Assert.True(history.Success, history.Message);
            Assert.Contains(history.Data!.Items, e => e.ActionCode == "incident" && e.Reason == null);
        }
        // Existing tenant-scoped settings supply an optional campus admission ceiling.
        // It is checked under the same serializable transaction as badge uniqueness.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            db.TenantSettings.Add(new TenantSetting
            {
                TenantId = tenantId, Key = "Facilities.Visitor.MaxConcurrent." + campusId,
                Value = "1", Category = "Facilities", CreatedAt = DateTime.UtcNow,
                CreatedBy = managerId
            });
            await db.SaveChangesAsync();
        }
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromMinutes(50)));
            var duplicate = await service.TransitionAsync(second.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion, BadgeCode = "GUEST-01" });
            Assert.Equal(409, duplicate.StatusCode);
            var capped = await service.TransitionAsync(second.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion, BadgeCode = "GUEST-02" });
            Assert.Equal(409, capped.StatusCode);
            var occupancy = await service.OccupancyAsync(campusId);
            Assert.True(occupancy.Success, occupancy.Message);
            Assert.Equal(1, occupancy.Data!.CheckedInCount);
            Assert.Equal(1, occupancy.Data.MaxConcurrent);
            Assert.Equal(403, (await service.OccupancyAsync(campusId + 999)).StatusCode);
            Assert.Equal(0, await db.Set<FacilityVisitorEvent>().CountAsync(x =>
                x.FacilityVisitorVisitId == second.Id && x.ActionCode == "checkin"));
            var checkout = await service.TransitionAsync(first.Id, "checkout",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = first.RowVersion });
            Assert.True(checkout.Success, checkout.Message);
            var policyRow = await db.TenantSettings.SingleAsync(s =>
                s.Key == "Facilities.Visitor.MaxConcurrent." + campusId);
            policyRow.Value = "invalid";
            await db.SaveChangesAsync();
            var invalidPolicy = await service.TransitionAsync(second.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion, BadgeCode = "GUEST-02" });
            Assert.Equal(409, invalidPolicy.StatusCode);
            Assert.Equal(409, (await service.OccupancyAsync(campusId)).StatusCode);
            policyRow.Value = "1";
            await db.SaveChangesAsync();
            var after = await service.TransitionAsync(second.Id, "checkin",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = second.RowVersion, BadgeCode = "GUEST-01" });
            Assert.True(after.Success, after.Message);
            Assert.Equal(1, await db.Set<FacilityVisitorVisit>().CountAsync(x =>
                x.CampusId == campusId && x.StateCode == "CheckedIn" && x.BadgeCode == "GUEST-01"));
            Assert.Equal(1, await db.Set<FacilityVisitorEvent>().CountAsync(x =>
                x.FacilityVisitorVisitId == first.Id && x.ActionCode == "checkout"));
            var overdue = await Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromHours(4))).OccupancyAsync(campusId);
            Assert.True(overdue.Success, overdue.Message);
            Assert.Equal(1, overdue.Data!.CheckedInCount);
            Assert.Equal(1, overdue.Data.OverdueCount);
            Assert.Equal(0, (await service.OccupancyAsync(campusId)).Data!.OverdueCount);
            var late = await Service(db, tenantId, managerId, "FacilitiesManager",
                new ShiftedClock(TimeSpan.FromHours(26))).TransitionAsync(first.Id, "incident",
                new FacilityVisitorTransitionDto
                { ClientCommandId = Guid.NewGuid(), RowVersion = checkout.Data!.RowVersion,
                    Reason = "Late attempted incident report." });
            Assert.Equal(409, late.StatusCode);
            Assert.Equal(1, await db.Set<FacilityVisitorEvent>().CountAsync(e =>
                e.FacilityVisitorVisitId == first.Id && e.ActionCode == "incident"));
        }

        // Bounded, manager-only summary omits visitor PII, badge and incident narrative.
        await using (var db = Db(options, tenantId, managerId, "FacilitiesManager"))
        {
            var service = Service(db, tenantId, managerId, "FacilitiesManager");
            var snapshot = await service.ExportSnapshotAsync(campusId,
                start.AddMinutes(-1), end.AddMinutes(1));
            Assert.True(snapshot.Success, snapshot.Message);
            Assert.Equal(2, snapshot.Data!.Count);
            Assert.Equal(1, snapshot.Data.Single(x => x.VisitId == first.Id).IncidentCount);
            // Only a successful authorized snapshot creates one PII-free access audit.
            var exportAudits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.Action == "VisitorSummaryExport")
                .ToListAsync();
            var exportAudit = Assert.Single(exportAudits);
            Assert.Equal(managerId, exportAudit.UserId);
            Assert.Equal(campusId, exportAudit.EntityId);
            Assert.Contains("Rows=2", exportAudit.NewValue);
            Assert.DoesNotContain("Guest One", exportAudit.NewValue);
            Assert.DoesNotContain("GUEST-01", exportAudit.NewValue);
            var serialized = System.Text.Json.JsonSerializer.Serialize(snapshot.Data);
            Assert.DoesNotContain("Guest One", serialized);
            Assert.DoesNotContain("Scheduled campus meeting", serialized);
            Assert.DoesNotContain("GUEST-01", serialized);
            Assert.DoesNotContain("Observed unauthorized badge transfer", serialized);
            Assert.Equal(400, (await service.ExportSnapshotAsync(campusId,
                start.AddDays(-32), end)).StatusCode);
            Assert.Equal(400, (await service.ExportSnapshotAsync(campusId,
                DateTime.SpecifyKind(start, DateTimeKind.Unspecified), end)).StatusCode);
            Assert.Equal(403, (await service.ExportSnapshotAsync(campusId + 999,
                start.AddMinutes(-1), end)).StatusCode);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
                a.TenantId == tenantId && a.Action == "VisitorSummaryExport"));
        }
        await using (var db = Db(options, tenantId, hostId, "Staff"))
            Assert.Equal(403, (await Service(db, tenantId, hostId, "Staff")
                .ExportSnapshotAsync(campusId, start.AddMinutes(-1), end)).StatusCode);
        await using (var db = Db(options, otherTenantId, outsiderId, "FacilitiesManager"))
            Assert.Equal(403, (await Service(db, otherTenantId, outsiderId, "FacilitiesManager")
                .ExportSnapshotAsync(campusId, start.AddMinutes(-1), end)).StatusCode);
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
