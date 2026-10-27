using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerStudentStatusTransitionIntegrationTests
{
    [Fact]
    public async Task Concurrent_status_changes_preserve_one_history_and_replay_is_idempotent()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, userId, enrollmentId;
        Guid studentReference;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-STATUS-" + suffix, Name = "Student Status CI",
                Email = "status-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "status-admin-" + suffix + "@example.test";
            var manager = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Status Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            var person = new Person { FullName = "Status CI Student" };
            setup.AddRange(tenant, manager, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; userId = manager.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "MAIN", Name = "Main Campus" };
                var year = new AcademicYear { TenantId = tenantId, Code = "2026", Name = "Academic 2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "SEC" };
                var student = new Student { TenantId = tenantId, PersonId = person.Id,
                    FullName = "Status CI Student", StudentCode = "ST-" + suffix,
                    AdmissionDate = new DateOnly(2026, 1, 1), StatusCode = "Active" };
                setup.AddRange(campus, year, program, student);
                await setup.SaveChangesAsync();
                studentReference = student.PublicId;
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Grade Nine", Code = "G9", LevelNo = 9 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId,
                    AcademicProgramId = program.Id, Name = "Current", Code = "CURRENT",
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, Name = "Grade Nine A", Code = "G9-A", Capacity = 40 };
                setup.AcademicBatches.Add(batch);
                await setup.SaveChangesAsync();
                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    AcademicYearId = year.Id, CampusId = campus.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                    AcademicCurriculumId = curriculum.Id, RollNo = "11",
                    EnrollmentDate = new DateOnly(2026, 1, 1),
                    State = EnrollmentState.Active, IsCurrent = true,
                    ClientRequestId = Guid.NewGuid() };
                setup.StudentEnrollments.Add(enrollment);
                await setup.SaveChangesAsync();
                enrollmentId = enrollment.Id;
            }
        }

        await using var firstDb = TenantDb(options, tenantId);
        await using var secondDb = TenantDb(options, tenantId);
        var first = Service(firstDb, tenantId, userId);
        var second = Service(secondDb, tenantId, userId);
        var before = await first.GetAsync(studentReference);
        Assert.True(before.Success, before.Message);
        Assert.Equal(8, Convert.FromBase64String(before.Data!.RowVersion).Length);

        var a = Request(before.Data.RowVersion);
        var b = Request(before.Data.RowVersion);
        var responses = await Task.WhenAll(
            first.ChangeAsync(studentReference, a),
            second.ChangeAsync(studentReference, b));
        Assert.Single(responses, x => x.Success);
        Assert.Single(responses, x => x.StatusCode == 409);
        var winner = responses[0].Success ? a : b;

        await using var replayDb = TenantDb(options, tenantId);
        var replay = await Service(replayDb, tenantId, userId).ChangeAsync(studentReference, winner);
        Assert.True(replay.Success, replay.Message);

        await using var staleDb = TenantDb(options, tenantId);
        var stale = Request(before.Data.RowVersion);
        stale.TargetStatus = "Active";
        var conflict = await Service(staleDb, tenantId, userId).ChangeAsync(studentReference, stale);
        Assert.Equal(409, conflict.StatusCode);
        var denied = await Service(staleDb, tenantId + 1, userId)
            .GetAsync(studentReference);
        Assert.Equal(404, denied.StatusCode);

        await using var verify = TenantDb(options, tenantId);
        Assert.Equal(1, await verify.StudentStatusHistories.CountAsync(x =>
            x.TenantId == tenantId && x.ToStatusCode == "Suspended"));
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x =>
            x.TenantId == tenantId && x.Scope == "StudentOperationalStatus"));
        Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == tenantId && x.Action == "Student.StatusChanged"));
        var enrollmentAfter = await verify.StudentEnrollments.SingleAsync(x => x.Id == enrollmentId);
        Assert.True(enrollmentAfter.IsCurrent);
        Assert.Equal(EnrollmentState.Active, enrollmentAfter.State);
        Assert.Equal("11", enrollmentAfter.RollNo);
    }

    private static ChangeStudentOperationalStatusRequestDto Request(string version) => new()
    {
        ClientRequestId = Guid.NewGuid(), RowVersion = version,
        TargetStatus = "Suspended", Reason = "Approved temporary student suspension"
    };

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }

    private static StudentStatusTransitionService Service(EduOSDbContext db, long tenantId, long userId) =>
        new(db, new TenantAdmin(tenantId, userId), NullLogger<StudentStatusTransitionService>.Instance);

    private sealed class TenantAdmin(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Status SQL Integration";
        public string? Email => "status-ci@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Student Status SQL CI";
    }
}
