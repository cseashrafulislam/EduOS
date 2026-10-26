using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class SqlServerStudentLifecycleIntegrationTests
{
    [Fact]
    public async Task Final_transfer_closes_current_enrollment_and_issues_single_certified_audited_exit()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, studentId, enrollmentId, userId;
        Guid studentRef;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-TC-" + suffix, Name = "Student Lifecycle CI",
                Email = "ci-tc-" + suffix + "@example.test", State = TenantState.Active };
            var username = "tc-manager-" + suffix + "@example.test";
            var manager = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "TC Manager", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            var person = new Person { FullName = "Lifecycle CI Student" };
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
                    FullName = "Lifecycle CI Student", StudentCode = "TC-" + suffix,
                    AdmissionDate = new DateOnly(2026, 1, 1), StatusCode = "Active" };
                setup.AddRange(campus, year, program, student);
                await setup.SaveChangesAsync();
                studentId = student.Id; studentRef = student.PublicId;
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
                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = studentId,
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
        await using var db = TenantDb(options, tenantId);
        var version = await db.Students.AsNoTracking().Where(x => x.Id == studentId)
            .Select(x => x.RowVersion).SingleAsync();
        Assert.Equal(8, version.Length);
        var request = new ProcessStudentExitDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = studentRef,
            StudentRowVersion = Convert.ToBase64String(version),
            ExitType = "Transfer", Reason = "Transfer to another institution"
        };
        var service = Service(db, tenantId, userId);
        var processed = await service.ProcessAsync(request);
        Assert.True(processed.Success, processed.Message);
        Assert.NotNull(processed.Data!.CertificateNo);
        var replay = await service.ProcessAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(processed.Data.CertificateNo, replay.Data!.CertificateNo);

        db.ChangeTracker.Clear();
        Assert.Equal("TC", await db.Students.Where(x => x.Id == studentId)
            .Select(x => x.StatusCode).SingleAsync());
        var enrollmentAfter = await db.StudentEnrollments.SingleAsync(x => x.Id == enrollmentId);
        Assert.False(enrollmentAfter.IsCurrent);
        Assert.Equal(EnrollmentState.Transferred, enrollmentAfter.State);
        Assert.Single(await db.StudentExitRecords.Where(x => x.StudentId == studentId).ToListAsync());
        Assert.Single(await db.TransferCertificates.Where(x => x.StudentId == studentId).ToListAsync());
        var statusHistory = await db.StudentStatusHistories.SingleAsync(x => x.StudentId == studentId);
        Assert.Equal("Active", statusHistory.FromStatusCode);
        Assert.Equal("TC", statusHistory.ToStatusCode);
        Assert.Equal(userId, statusHistory.ChangedByUserId);
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }
    private static StudentExitService Service(EduOSDbContext db, long tenantId, long userId) => new(
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<StudentExitRecord>(db), new GenericRepository<TransferCertificate>(db),
        new GenericRepository<StudentStatusHistory>(db), new GenericRepository<StudentInvoice>(db),
        db, new TenantAdmin(tenantId, userId), TimeProvider.System, NullLogger<StudentExitService>.Instance);

    private sealed class TenantAdmin(long tenantId, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "TC SQL Integration";
        public string? Email => "tc-ci@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Student Lifecycle SQL CI";
    }
}
