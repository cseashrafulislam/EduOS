using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Alumni;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerAlumniConsentIntegrationTests
{
    [Fact]
    public async Task Completed_graduate_controls_directory_visibility_with_replay_concurrency_and_tenant_isolation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, userId, publisherId, otherTenantId, campusId;
        Guid studentReference;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-AL-" + suffix, Name = "Alumni SQL CI",
                Email = "alumni-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-AX-" + suffix, Name = "Other alumni tenant",
                Email = "other-alumni-" + suffix + "@example.test", State = TenantState.Active };
            var username = "alumni-" + suffix + "@example.test";
            var user = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Verified Graduate", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            var approverName = "alumni-approver-" + suffix + "@example.test";
            var approver = new ApplicationUser
            {
                UserName = approverName, NormalizedUserName = approverName.ToUpperInvariant(),
                Email = approverName, NormalizedEmail = approverName.ToUpperInvariant(),
                FullName = "Second Alumni Administrator", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            var person = new Person { FullName = "Verified Graduate" };
            setup.AddRange(tenant, other, user, approver, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id; userId = user.Id; publisherId = approver.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "MAIN", Name = "Main Campus" };
                var year = new AcademicYear { TenantId = tenantId, Code = "2026", Name = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "College", Code = "COL" };
                var student = new Student { TenantId = tenantId, PersonId = person.Id, UserId = userId,
                    FullName = "Verified Graduate", StudentCode = "AL-" + suffix,
                    AdmissionDate = new DateOnly(2026, 1, 1), StatusCode = "Passed" };
                setup.AddRange(campus, year, program, student);
                await setup.SaveChangesAsync();
                studentReference = student.PublicId; campusId = campus.Id;
                // Strict Alumni admin policy requires an explicit active campus grant.
                setup.UserCampusAccesses.AddRange(
                    new UserCampusAccess { TenantId = tenantId, UserId = userId,
                        CampusId = campus.Id, IsActive = true },
                    new UserCampusAccess { TenantId = tenantId, UserId = publisherId,
                        CampusId = campus.Id, IsActive = true });
                await setup.SaveChangesAsync();
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Final", Code = "FINAL", LevelNo = 12 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId,
                    AcademicProgramId = program.Id, Name = "Final", Code = "FINAL",
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, Name = "Final A", Code = "FINAL-A", Capacity = 40 };
                setup.Add(batch);
                await setup.SaveChangesAsync();
                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    AcademicYearId = year.Id, CampusId = campus.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                    AcademicCurriculumId = curriculum.Id, RollNo = "11",
                    EnrollmentDate = new DateOnly(2026, 1, 1),
                    State = EnrollmentState.Completed, IsCurrent = false,
                    ClientRequestId = Guid.NewGuid() };
                setup.Add(enrollment);
                await setup.SaveChangesAsync();
                setup.Add(new StudentExitRecord
                {
                    TenantId = tenantId, StudentId = student.Id, StudentEnrollmentId = enrollment.Id,
                    ExitType = StudentExitType.Completed, FeesCleared = true,
                    ProcessedByUserId = userId, ClientRequestId = Guid.NewGuid()
                });
                await setup.SaveChangesAsync();
            }
        }

        await using var db = TenantDb(options, tenantId);
        var graduate = Service(db, tenantId, userId, false);
        var admin = Service(db, tenantId, userId, true);
        var first = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = false, ContactEmail = "invalid-contact-is-ignored"
        });
        Assert.True(first.Success, first.Message);
        Assert.False(first.Data!.DirectoryOptIn);
        Assert.Null(first.Data.ContactEmail);

        var before = await admin.DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.True(before.Success, before.Message);
        Assert.Empty(before.Data!.Items);

        var optInRequest = new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = true, ContactEmail = "graduate@example.test",
            RowVersion = first.Data.RowVersion
        };
        var opted = await graduate.SaveConsentAsync(optInRequest);
        Assert.True(opted.Success, opted.Message);
        var replay = await graduate.SaveConsentAsync(optInRequest);
        Assert.True(replay.Success, replay.Message);

        var stale = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = false, RowVersion = first.Data.RowVersion
        });
        Assert.False(stale.Success);
        Assert.Equal(409, stale.StatusCode);

        var directory = await admin.DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.True(directory.Success, directory.Message);
        Assert.Single(directory.Data!.Items);
        Assert.Equal("graduate@example.test", directory.Data.Items[0].ContactEmail);


        var eventDraftRequest = new AlumniEventDraftDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campusId,
            Title = "Verified alumni reunion",
            Description = "Alumni engagement integration test",
            StartsAtUtc = DateTime.UtcNow.AddDays(3),
            EndsAtUtc = DateTime.UtcNow.AddDays(3).AddHours(2)
        };
        var draft = await admin.CreateEventAsync(eventDraftRequest);
        Assert.True(draft.Success, draft.Message);
        Assert.Equal(1, draft.Data!.Status);
        var draftReplay = await admin.CreateEventAsync(eventDraftRequest);
        Assert.True(draftReplay.Success, draftReplay.Message);
        Assert.Equal(draft.Data.Reference, draftReplay.Data!.Reference);

        var selfApproval = await admin.ChangeEventStatusAsync(draft.Data.Reference,
            new AlumniEventActionDto { RowVersion = draft.Data.RowVersion }, true);
        Assert.False(selfApproval.Success);
        Assert.Equal(403, selfApproval.StatusCode);

        var publisherService = Service(db, tenantId, publisherId, true);
        var published = await publisherService.ChangeEventStatusAsync(draft.Data.Reference,
            new AlumniEventActionDto { RowVersion = draft.Data.RowVersion }, true);
        Assert.True(published.Success, published.Message);
        Assert.Equal(2, published.Data!.Status);

        var eventPage = await graduate.EventsAsync(new AlumniEventQueryDto());
        Assert.True(eventPage.Success, eventPage.Message);
        Assert.Contains(eventPage.Data!.Items, e => e.Reference == draft.Data.Reference);

        var rsvpRequest = new AlumniEventRsvpDto
        {
            ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
            StudentReference = studentReference, Response = 1
        };
        var rsvp = await graduate.RsvpAsync(rsvpRequest);
        Assert.True(rsvp.Success, rsvp.Message);
        Assert.Equal(1, rsvp.Data!.MyResponse);
        var rsvpReplay = await graduate.RsvpAsync(rsvpRequest);
        Assert.True(rsvpReplay.Success, rsvpReplay.Message);
        // The first Going command is no longer idempotently valid after a
        // Declined -> Going round trip, even though the final response matches.
        var declined = await graduate.RsvpAsync(new AlumniEventRsvpDto
        {
            ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
            StudentReference = studentReference, Response = 2,
            RowVersion = rsvp.Data.MyRowVersion
        });
        Assert.True(declined.Success, declined.Message);
        var goingAgain = await graduate.RsvpAsync(new AlumniEventRsvpDto
        {
            ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
            StudentReference = studentReference, Response = 1,
            RowVersion = declined.Data!.MyRowVersion
        });
        Assert.True(goingAgain.Success, goingAgain.Message);
        var supersededRsvp = await graduate.RsvpAsync(rsvpRequest);
        Assert.False(supersededRsvp.Success);
        Assert.Equal(409, supersededRsvp.StatusCode);
        var staleRsvp = await graduate.RsvpAsync(new AlumniEventRsvpDto
        {
            ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
            StudentReference = studentReference, Response = 2
        });
        Assert.False(staleRsvp.Success);
        Assert.Equal(409, staleRsvp.StatusCode);

        // A login may own multiple completed student records. The selected graduate
        // must own the displayed RSVP/version; another identity must not bleed in.
        var originalStudent = await db.Students.SingleAsync(x => x.PublicId == studentReference);
        var originalEnrollment = await db.StudentEnrollments.SingleAsync(x => x.StudentId == originalStudent.Id);
        var alternatePerson = new Person { FullName = "Alternate Verified Graduate" };
        db.Add(alternatePerson);
        await db.SaveChangesAsync();
        var alternateStudent = new Student
        {
            TenantId = tenantId, PersonId = alternatePerson.Id, UserId = userId,
            FullName = "Alternate Verified Graduate", StudentCode = "AL-ALT-" + suffix,
            AdmissionDate = new DateOnly(2026, 1, 1), StatusCode = "Passed"
        };
        db.Add(alternateStudent);
        await db.SaveChangesAsync();
        var alternateEnrollment = new StudentEnrollment
        {
            TenantId = tenantId, StudentId = alternateStudent.Id,
            AcademicYearId = originalEnrollment.AcademicYearId,
            CampusId = originalEnrollment.CampusId,
            AcademicProgramId = originalEnrollment.AcademicProgramId,
            AcademicLevelId = originalEnrollment.AcademicLevelId,
            AcademicBatchId = originalEnrollment.AcademicBatchId,
            AcademicCurriculumId = originalEnrollment.AcademicCurriculumId,
            RollNo = "12", EnrollmentDate = new DateOnly(2026, 1, 1),
            State = EnrollmentState.Completed, IsCurrent = false,
            ClientRequestId = Guid.NewGuid()
        };
        db.Add(alternateEnrollment);
        await db.SaveChangesAsync();
        db.Add(new StudentExitRecord
        {
            TenantId = tenantId, StudentId = alternateStudent.Id,
            StudentEnrollmentId = alternateEnrollment.Id, ExitType = StudentExitType.Completed,
            FeesCleared = true, ProcessedByUserId = userId, ClientRequestId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        var alternateConsent = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = alternateStudent.PublicId, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = true
        });
        Assert.True(alternateConsent.Success, alternateConsent.Message);
        var alternateRsvp = await graduate.RsvpAsync(new AlumniEventRsvpDto
        {
            ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
            StudentReference = alternateStudent.PublicId, Response = 2
        });
        Assert.True(alternateRsvp.Success, alternateRsvp.Message);
        var firstSelection = await graduate.EventsAsync(new AlumniEventQueryDto
        {
            StudentReference = studentReference
        });
        Assert.True(firstSelection.Success, firstSelection.Message);
        Assert.Equal(1, Assert.Single(firstSelection.Data!.Items).MyResponse);
        var alternateSelection = await graduate.EventsAsync(new AlumniEventQueryDto
        {
            StudentReference = alternateStudent.PublicId
        });
        Assert.True(alternateSelection.Success, alternateSelection.Message);
        Assert.Equal(2, Assert.Single(alternateSelection.Data!.Items).MyResponse);
        var guessedSelection = await graduate.EventsAsync(new AlumniEventQueryDto
        {
            StudentReference = Guid.NewGuid()
        });
        Assert.True(guessedSelection.Success, guessedSelection.Message);
        Assert.Empty(guessedSelection.Data!.Items);
        var otherTenantSelection = await Service(db, otherTenantId, userId, false)
            .EventsAsync(new AlumniEventQueryDto { StudentReference = studentReference });
        Assert.True(otherTenantSelection.Success, otherTenantSelection.Message);
        Assert.Empty(otherTenantSelection.Data!.Items);

        var engagement = await admin.EventsAsync(new AlumniEventQueryDto());
        Assert.True(engagement.Success, engagement.Message);
        Assert.Equal(1, Assert.Single(engagement.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference)).GoingCount);

        // A campus-restricted administrator must not see graduates from another campus.
        var otherCampus = new Campus { TenantId = tenantId, Code = "SECOND", Name = "Second Campus" };
        db.Campuses.Add(otherCampus);
        await db.SaveChangesAsync();
        var originalGrant = await db.UserCampusAccesses.SingleAsync(x =>
            x.TenantId == tenantId && x.UserId == userId && x.CampusId == campusId);
        originalGrant.IsActive = false;
        await db.SaveChangesAsync();
        var withoutGrant = await admin.EventCampusesAsync();
        Assert.True(withoutGrant.Success, withoutGrant.Message);
        Assert.Empty(withoutGrant.Data!);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = tenantId, UserId = userId, CampusId = otherCampus.Id, IsActive = true
        });
        await db.SaveChangesAsync();
        var campusRestricted = await admin.DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.True(campusRestricted.Success, campusRestricted.Message);
        Assert.Empty(campusRestricted.Data!.Items);
        var restrictedEvents = await admin.EventsAsync(new AlumniEventQueryDto());
        Assert.True(restrictedEvents.Success, restrictedEvents.Message);
        Assert.DoesNotContain(restrictedEvents.Data!.Items,
            e => e.Reference == draft.Data.Reference);
        var restrictedCancel = await admin.ChangeEventStatusAsync(draft.Data.Reference,
            new AlumniEventActionDto { RowVersion = published.Data.RowVersion }, false);
        Assert.False(restrictedCancel.Success);
        Assert.Equal(404, restrictedCancel.StatusCode);

        var foreignEvents = await Service(db, otherTenantId, userId, true)
            .EventsAsync(new AlumniEventQueryDto());
        Assert.True(foreignEvents.Success, foreignEvents.Message);
        Assert.Empty(foreignEvents.Data!.Items);
        var foreignRsvp = await Service(db, otherTenantId, userId, false)
            .RsvpAsync(new AlumniEventRsvpDto
            {
                ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
                StudentReference = studentReference, Response = 1
            });
        Assert.False(foreignRsvp.Success);
        Assert.Equal(404, foreignRsvp.StatusCode);

        var foreign = await Service(db, otherTenantId, userId, true)
            .DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.True(foreign.Success, foreign.Message);
        Assert.Empty(foreign.Data!.Items);

        var revokeRequest = new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = false, RowVersion = opted.Data!.RowVersion
        };
        var revoked = await graduate.SaveConsentAsync(revokeRequest);
        Assert.True(revoked.Success, revoked.Message);
        Assert.Null(revoked.Data!.ContactEmail);
        // Replaying an old grant after revocation must not report a successful opt-in.
        var staleGrantReplay = await graduate.SaveConsentAsync(optInRequest);
        Assert.False(staleGrantReplay.Success);
        Assert.Equal(409, staleGrantReplay.StatusCode);
        var clearedEngagement = await publisherService.EventsAsync(new AlumniEventQueryDto());
        Assert.True(clearedEngagement.Success, clearedEngagement.Message);
        Assert.Equal(0, Assert.Single(clearedEngagement.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference)).GoingCount);
        var hiddenEvents = await graduate.EventsAsync(new AlumniEventQueryDto
        {
            StudentReference = studentReference
        });
        Assert.Empty(hiddenEvents.Data!.Items);
        var after = await admin.DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.Empty(after.Data!.Items);

        // Revocation deletes the RSVP but preserves its privacy-safe audit command.
        // Re-consenting must not make the old request key appear to have restored attendance.
        var reOpted = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = true, RowVersion = revoked.Data.RowVersion
        });
        Assert.True(reOpted.Success, reOpted.Message);
        // An old revoke retry must not claim that the newer opt-in was withdrawn.
        var staleRevokeReplay = await graduate.SaveConsentAsync(revokeRequest);
        Assert.False(staleRevokeReplay.Success);
        Assert.Equal(409, staleRevokeReplay.StatusCode);
        var currentConsent = await graduate.MineAsync();
        Assert.True(currentConsent.Success, currentConsent.Message);
        Assert.True(Assert.Single(currentConsent.Data!.Where(x =>
            x.StudentReference == studentReference)).DirectoryOptIn);
        var revokedReplay = await graduate.RsvpAsync(rsvpRequest);
        Assert.False(revokedReplay.Success);
        Assert.Equal(409, revokedReplay.StatusCode);
        var afterReplay = await publisherService.EventsAsync(new AlumniEventQueryDto());
        Assert.Equal(0, Assert.Single(afterReplay.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference)).GoingCount);

        var freshRsvp = await graduate.RsvpAsync(new AlumniEventRsvpDto
        {
            ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
            StudentReference = studentReference, Response = 1
        });
        Assert.True(freshRsvp.Success, freshRsvp.Message);
        Assert.Equal(1, freshRsvp.Data!.MyResponse);

        var roster = await publisherService.AttendanceAsync(draft.Data.Reference,
            new AlumniEventAttendanceQueryDto());
        Assert.True(roster.Success, roster.Message);
        var attendee = Assert.Single(roster.Data!.Items);
        Assert.Equal(studentReference, attendee.StudentReference);
        var tooEarly = await publisherService.CheckInAsync(draft.Data.Reference, new AlumniEventCheckInDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = studentReference,
            Present = true, RowVersion = attendee.RowVersion
        });
        Assert.Equal(404, tooEarly.StatusCode);
        // Synthetic CI event only; no customer or production writes.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.AlumniEvents SET StartsAtUtc = {DateTime.UtcNow.AddMinutes(-15)},
                EndsAtUtc = {DateTime.UtcNow.AddHours(2)}
            WHERE TenantId = {tenantId} AND PublicId = {draft.Data.Reference}");
        var request = new AlumniEventCheckInDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = studentReference,
            Present = true, RowVersion = attendee.RowVersion
        };
        var checkedIn = await publisherService.CheckInAsync(draft.Data.Reference, request);
        Assert.True(checkedIn.Success, checkedIn.Message);
        Assert.NotNull(checkedIn.Data!.CheckedInAtUtc);
        Assert.True((await publisherService.CheckInAsync(draft.Data.Reference, request)).Success);
        var staleCheckIn = await publisherService.CheckInAsync(draft.Data.Reference,
            new AlumniEventCheckInDto
            {
                ClientRequestId = Guid.NewGuid(), StudentReference = studentReference,
                Present = false, RowVersion = attendee.RowVersion
            });
        Assert.Equal(409, staleCheckIn.StatusCode);
        var crossTenantRoster = await Service(db, otherTenantId, publisherId, true)
            .AttendanceAsync(draft.Data.Reference, new AlumniEventAttendanceQueryDto());
        Assert.Equal(404, crossTenantRoster.StatusCode);
        var restrictedRoster = await admin.AttendanceAsync(draft.Data.Reference,
            new AlumniEventAttendanceQueryDto());
        Assert.Equal(404, restrictedRoster.StatusCode);
        var engagementWithAttendance = await publisherService.EventsAsync(new AlumniEventQueryDto());
        Assert.Equal(1, Assert.Single(engagementWithAttendance.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference)).CheckedInCount);
        var reversed = await publisherService.CheckInAsync(draft.Data.Reference,
            new AlumniEventCheckInDto
            {
                ClientRequestId = Guid.NewGuid(), StudentReference = studentReference,
                Present = false, RowVersion = checkedIn.Data.RowVersion
            });
        Assert.True(reversed.Success, reversed.Message);
        Assert.Null(reversed.Data!.CheckedInAtUtc);
        var checkedAgain = await publisherService.CheckInAsync(draft.Data.Reference,
            new AlumniEventCheckInDto
            {
                ClientRequestId = Guid.NewGuid(), StudentReference = studentReference,
                Present = true, RowVersion = reversed.Data.RowVersion
            });
        Assert.True(checkedAgain.Success, checkedAgain.Message);
        // The original check-in key must not be accepted after undo + check-in.
        var supersededCheckIn = await publisherService.CheckInAsync(draft.Data.Reference, request);
        Assert.False(supersededCheckIn.Success);
        Assert.Equal(409, supersededCheckIn.StatusCode);

        // A subsequently invalidated V1 completion must disappear immediately from
        // aggregated engagement reporting and the attendance roster, even if an RSVP
        // and check-in still physically exist. This modifies synthetic CI data only.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.StudentExitRecords SET FeesCleared = {false}
            WHERE TenantId = {tenantId} AND StudentId IN
                (SELECT Id FROM dbo.Students WHERE TenantId = {tenantId}
                    AND PublicId = {studentReference})");
        var invalidatedReport = await publisherService.EventsAsync(new AlumniEventQueryDto());
        Assert.True(invalidatedReport.Success, invalidatedReport.Message);
        var invalidatedEvent = Assert.Single(invalidatedReport.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference));
        Assert.Equal(0, invalidatedEvent.GoingCount);
        Assert.Equal(1, invalidatedEvent.DeclinedCount); // Alternate eligible graduate remains.
        Assert.Equal(0, invalidatedEvent.CheckedInCount);
        var invalidatedRoster = await publisherService.AttendanceAsync(draft.Data.Reference,
            new AlumniEventAttendanceQueryDto());
        Assert.True(invalidatedRoster.Success, invalidatedRoster.Message);
        Assert.Empty(invalidatedRoster.Data!.Items);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.StudentExitRecords SET FeesCleared = {true}
            WHERE TenantId = {tenantId} AND StudentId IN
                (SELECT Id FROM dbo.Students WHERE TenantId = {tenantId}
                    AND PublicId = {studentReference})");
        var restoredReport = await publisherService.EventsAsync(new AlumniEventQueryDto());
        Assert.True(restoredReport.Success, restoredReport.Message);
        var restoredEvent = Assert.Single(restoredReport.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference));
        Assert.Equal(1, restoredEvent.GoingCount);
        Assert.Equal(1, restoredEvent.CheckedInCount);

        var finalRevoke = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = false, RowVersion = reOpted.Data!.RowVersion
        });
        Assert.True(finalRevoke.Success, finalRevoke.Message);
        var emptyRoster = await publisherService.AttendanceAsync(draft.Data.Reference,
            new AlumniEventAttendanceQueryDto());
        Assert.True(emptyRoster.Success, emptyRoster.Message);
        Assert.Empty(emptyRoster.Data!.Items);
        var clearedAttendance = await publisherService.EventsAsync(new AlumniEventQueryDto());
        Assert.Equal(0, Assert.Single(clearedAttendance.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference)).CheckedInCount);

        // Invitation consent is independent of publicly visible directory consent.
        var invalidInvitation = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            InvitationEmailOptIn = true, RowVersion = finalRevoke.Data!.RowVersion
        });
        Assert.False(invalidInvitation.Success);
        var inviteOnly = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = false, InvitationEmailOptIn = true,
            ContactEmail = "private-invites@example.test", RowVersion = finalRevoke.Data.RowVersion
        });
        Assert.True(inviteOnly.Success, inviteOnly.Message);
        Assert.False(inviteOnly.Data!.DirectoryOptIn);
        Assert.True(inviteOnly.Data.InvitationEmailOptIn);
        Assert.False(inviteOnly.Data.InvitationSmsOptIn);
        Assert.Equal("private-invites@example.test", inviteOnly.Data.ContactEmail);
        var inviteHidden = await publisherService.DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.True(inviteHidden.Success, inviteHidden.Message);
        // Other graduates may remain opted into the directory. Invitation-only
        // consent must hide THIS graduate without hiding unrelated records.
        Assert.DoesNotContain(inviteHidden.Data!.Items, x => x.StudentCode == "AL-" + suffix);

        // Provider-free deterministic SQL acceptance: only the explicit opted-in
        // graduate is eligible, and a fake transport prevents all actual egress.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE dbo.AlumniEvents SET StartsAtUtc = {DateTime.UtcNow.AddDays(2)},
                EndsAtUtc = {DateTime.UtcNow.AddDays(2).AddHours(2)}
            WHERE TenantId = {tenantId} AND PublicId = {draft.Data.Reference}");
        var beforeGateway = await publisherService.InvitationSummaryAsync(draft.Data.Reference, 2);
        Assert.True(beforeGateway.Success, beforeGateway.Message);
        Assert.False(beforeGateway.Data!.GatewayReady);
        Assert.Equal(1, beforeGateway.Data.EligibleCount);
        var noGateway = await publisherService.SendInvitationsAsync(draft.Data.Reference,
            new AlumniInvitationSendDto { ClientRequestId = Guid.NewGuid(), Channel = 2, Confirmed = true });
        Assert.Equal(409, noGateway.StatusCode);
        db.CommunicationGateways.Add(new CommunicationGateway
        {
            TenantId = tenantId, ProviderCode = "SMTP", Channel = NotificationChannelType.Email,
            IsActive = true, IsDefault = true, ProtectedCredential = "fixture-no-real-credential",
            SmtpHost = "smtp.example.test", SmtpPort = 587, SmtpUsername = "fixture",
            FromEmail = "alumni@example.test", UseSsl = true
        });
        await db.SaveChangesAsync();
        var wrongCampus = await admin.InvitationSummaryAsync(draft.Data.Reference, 2);
        Assert.Equal(404, wrongCampus.StatusCode);
        var wrongTenant = await Service(db, otherTenantId, publisherId, true)
            .InvitationSummaryAsync(draft.Data.Reference, 2);
        Assert.Equal(404, wrongTenant.StatusCode);
        var invitationRequest = new AlumniInvitationSendDto
        {
            ClientRequestId = Guid.NewGuid(), Channel = 2, Confirmed = true
        };
        var invited = await publisherService.SendInvitationsAsync(draft.Data.Reference, invitationRequest);
        Assert.True(invited.Success, invited.Message);
        Assert.Equal(1, invited.Data!.ReservedCount);
        Assert.Equal(1, invited.Data.AcceptedCount);
        var idempotent = await publisherService.SendInvitationsAsync(draft.Data.Reference, invitationRequest);
        Assert.True(idempotent.Success, idempotent.Message);
        Assert.True(idempotent.Data!.AlreadyProcessed);
        Assert.Equal(1, idempotent.Data.AcceptedCount);
        var nextBatch = await publisherService.SendInvitationsAsync(draft.Data.Reference,
            new AlumniInvitationSendDto { ClientRequestId = Guid.NewGuid(), Channel = 2, Confirmed = true });
        Assert.True(nextBatch.Success, nextBatch.Message);
        Assert.Equal(0, nextBatch.Data!.ReservedCount);
        var smsNoGateway = await publisherService.SendInvitationsAsync(draft.Data.Reference,
            new AlumniInvitationSendDto { ClientRequestId = Guid.NewGuid(), Channel = 3, Confirmed = true });
        Assert.Equal(409, smsNoGateway.StatusCode);
        var inviteRevoked = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = false, RowVersion = inviteOnly.Data.RowVersion
        });
        Assert.True(inviteRevoked.Success, inviteRevoked.Message);
        Assert.False(inviteRevoked.Data!.InvitationEmailOptIn);
        Assert.Null(inviteRevoked.Data.ContactEmail);
        var afterInviteRevoke = await publisherService.InvitationSummaryAsync(draft.Data.Reference, 2);
        Assert.True(afterInviteRevoke.Success, afterInviteRevoke.Message);
        Assert.Equal(0, afterInviteRevoke.Data!.EligibleCount);
        Assert.Equal(1, afterInviteRevoke.Data.AcceptedCount); // immutable attempt evidence

        // Regression: the old Take(100) before GroupBy discarded a second eligible
        // graduate when the first student had 100+ completed-exit history rows.
        var secondPerson = new Person { FullName = "Second Verified Graduate" };
        db.Add(secondPerson);
        await db.SaveChangesAsync();
        // Reuse the original completed enrollment already loaded above.
        var secondStudent = new Student
        {
            TenantId = tenantId, PersonId = secondPerson.Id, UserId = userId,
            FullName = "Second Verified Graduate", StudentCode = "AL-B-" + suffix,
            AdmissionDate = new DateOnly(2026, 1, 1), StatusCode = "Passed"
        };
        db.Add(secondStudent);
        await db.SaveChangesAsync();
        var secondEnrollment = new StudentEnrollment
        {
            TenantId = tenantId, StudentId = secondStudent.Id,
            AcademicYearId = originalEnrollment.AcademicYearId,
            CampusId = originalEnrollment.CampusId,
            AcademicProgramId = originalEnrollment.AcademicProgramId,
            AcademicLevelId = originalEnrollment.AcademicLevelId,
            AcademicBatchId = originalEnrollment.AcademicBatchId,
            AcademicCurriculumId = originalEnrollment.AcademicCurriculumId,
            RollNo = "13", EnrollmentDate = new DateOnly(2026, 1, 1),
            State = EnrollmentState.Completed, IsCurrent = false,
            ClientRequestId = Guid.NewGuid()
        };
        db.Add(secondEnrollment);
        await db.SaveChangesAsync();
        var recent = DateTime.UtcNow;
        var repeatedExits = Enumerable.Range(0, 101).Select(i => new StudentExitRecord
        {
            TenantId = tenantId, StudentId = originalStudent.Id,
            StudentEnrollmentId = originalEnrollment.Id,
            ExitType = StudentExitType.Completed, FeesCleared = true,
            ProcessedByUserId = userId, ClientRequestId = Guid.NewGuid(),
            ProcessedAt = recent.AddMinutes(-i)
        }).ToList();
        repeatedExits.Add(new StudentExitRecord
        {
            TenantId = tenantId, StudentId = secondStudent.Id,
            StudentEnrollmentId = secondEnrollment.Id,
            ExitType = StudentExitType.Completed, FeesCleared = true,
            ProcessedByUserId = userId, ClientRequestId = Guid.NewGuid(),
            ProcessedAt = recent.AddDays(-1)
        });
        db.AddRange(repeatedExits);
        await db.SaveChangesAsync();

        var distinctGraduates = await graduate.MineAsync();
        Assert.True(distinctGraduates.Success, distinctGraduates.Message);
        // The fixture already includes a separate earlier completed student identity.
        Assert.Equal(3, distinctGraduates.Data!.Count);
        Assert.Contains(distinctGraduates.Data, x => x.StudentReference == studentReference);
        var otherGraduate = Assert.Single(distinctGraduates.Data,
            x => x.StudentReference == secondStudent.PublicId);
        Assert.False(otherGraduate.Registered);
        Assert.Null(otherGraduate.ContactEmail);
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }

    private static AlumniService Service(EduOSDbContext db, long tenantId, long userId, bool admin) =>
        new(db, new Actor(tenantId, userId, admin), NullLogger<AlumniService>.Instance,
            new FakeInvitationTransport());

    private sealed class FakeInvitationTransport : IAlumniInvitationTransport
    {
        public Task<AlumniGatewaySendResult> SendAsync(CommunicationGateway gateway,
            string destination, string subject, string text, CancellationToken ct)
        {
            Assert.Equal("private-invites@example.test", destination);
            Assert.Equal(NotificationChannelType.Email, gateway.Channel);
            return Task.FromResult(new AlumniGatewaySendResult(true, null));
        }
    }

    private sealed class Actor(long tenantId, long userId, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Alumni SQL CI";
        public string? Email => "alumni-ci@example.test";
        public bool IsTenantAdmin => admin;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Student"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Student";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Alumni SQL CI";
    }
}
