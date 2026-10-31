using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
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
        var staleRsvp = await graduate.RsvpAsync(new AlumniEventRsvpDto
        {
            ClientRequestId = Guid.NewGuid(), EventReference = draft.Data.Reference,
            StudentReference = studentReference, Response = 2
        });
        Assert.False(staleRsvp.Success);
        Assert.Equal(409, staleRsvp.StatusCode);

        var engagement = await admin.EventsAsync(new AlumniEventQueryDto());
        Assert.True(engagement.Success, engagement.Message);
        Assert.Equal(1, Assert.Single(engagement.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference)).GoingCount);

        // A campus-restricted administrator must not see graduates from another campus.
        var otherCampus = new Campus { TenantId = tenantId, Code = "SECOND", Name = "Second Campus" };
        db.Campuses.Add(otherCampus);
        await db.SaveChangesAsync();
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = tenantId, UserId = userId, CampusId = otherCampus.Id, IsActive = true
        });
        await db.SaveChangesAsync();
        var campusRestricted = await admin.DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.True(campusRestricted.Success, campusRestricted.Message);
        Assert.Empty(campusRestricted.Data!.Items);

        var foreign = await Service(db, otherTenantId, userId, true)
            .DirectoryAsync(new AlumniDirectoryQueryDto());
        Assert.True(foreign.Success, foreign.Message);
        Assert.Empty(foreign.Data!.Items);

        var revoked = await graduate.SaveConsentAsync(new AlumniConsentRequestDto
        {
            StudentReference = studentReference, ClientRequestId = Guid.NewGuid(),
            DirectoryOptIn = false, RowVersion = opted.Data!.RowVersion
        });
        Assert.True(revoked.Success, revoked.Message);
        Assert.Null(revoked.Data!.ContactEmail);
        var clearedEngagement = await publisherService.EventsAsync(new AlumniEventQueryDto());
        Assert.True(clearedEngagement.Success, clearedEngagement.Message);
        Assert.Equal(0, Assert.Single(clearedEngagement.Data!.Items.Where(e =>
            e.Reference == draft.Data.Reference)).GoingCount);
        var hiddenEvents = await graduate.EventsAsync(new AlumniEventQueryDto());
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
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }

    private static AlumniService Service(EduOSDbContext db, long tenantId, long userId, bool admin) =>
        new(db, new Actor(tenantId, userId, admin), NullLogger<AlumniService>.Instance);

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
