using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Real SQL Server seat allocation test; EF InMemory cannot prove row locks or rollback.</summary>
public sealed class SqlServerEnrollmentAllocationIntegrationTests
{
    [Fact]
    public async Task One_seat_is_allocated_once_and_rejected_retry_rolls_back_batch_version()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, batchId, curriculumId, managerUserId;
        Guid firstStudent, secondStudent;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-SEAT-" + suffix, Name = "Enrollment Seat CI",
                Email = "seat-" + suffix + "@example.test", State = TenantState.Active };
            var personA = new Person { FullName = "First CI Student" };
            var personB = new Person { FullName = "Second CI Student" };
            var username = "manager-" + suffix + "@example.test";
            var manager = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Enrollment SQL CI Manager", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.AddRange(tenant, personA, personB, manager);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; managerUserId = manager.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "SEC" };
                var studentA = new Student { TenantId = tenantId, PersonId = personA.Id,
                    FullName = "First CI Student", StudentCode = "CI-A-" + suffix,
                    AdmissionDate = new DateOnly(2026, 1, 1) };
                var studentB = new Student { TenantId = tenantId, PersonId = personB.Id,
                    FullName = "Second CI Student", StudentCode = "CI-B-" + suffix,
                    AdmissionDate = new DateOnly(2026, 1, 1) };
                setup.AddRange(campus, year, program, studentA, studentB);
                await setup.SaveChangesAsync();
                firstStudent = studentA.PublicId;
                secondStudent = studentB.PublicId;
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Grade 9", Code = "G9", LevelNo = 9 };
                var subject = new Subject { TenantId = tenantId, Name = "Mathematics", Code = "MATH" };
                var curriculum = new AcademicCurriculum { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Secondary 2026", Code = "SEC-26", EffectiveFrom = new DateOnly(2026, 1, 1),
                    IsCurrent = true, IsActive = true };
                setup.AddRange(level, subject, curriculum);
                await setup.SaveChangesAsync();
                curriculumId = curriculum.Id;
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, Name = "Nine A", Code = "G9-A", Capacity = 1 };
                var line = new CurriculumSubject { TenantId = tenantId,
                    AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
                    SubjectId = subject.Id, IsOptional = false, CreditHours = 4m };
                setup.AddRange(batch, line);
                await setup.SaveChangesAsync();
                batchId = batch.Id;
                setup.SubjectOfferings.Add(new SubjectOffering { TenantId = tenantId, AcademicBatchId = batchId,
                    AcademicYearId = year.Id, CurriculumSubjectId = line.Id, Code = "MATH-9A" });
                await setup.SaveChangesAsync();
            }
        }

        var firstRequest = new CreateStudentEnrollmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = firstStudent,
            AcademicBatchId = batchId, AcademicCurriculumId = curriculumId,
            RollNo = "01", EnrollmentDate = new DateOnly(2026, 9, 21)
        };
        var secondRequest = new CreateStudentEnrollmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = secondStudent,
            AcademicBatchId = batchId, AcademicCurriculumId = curriculumId,
            RollNo = "02", EnrollmentDate = new DateOnly(2026, 9, 21)
        };
        await using (var db = OpenTenantContext(options, tenantId))
        {
            var first = await Service(db, tenantId, managerUserId).EnrollAsync(firstRequest);
            Assert.True(first.Success, first.Message);
            var replay = await Service(db, tenantId, managerUserId).EnrollAsync(firstRequest);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(first.Data!.Reference, replay.Data!.Reference);
        }
        byte[] version;
        await using (var before = OpenTenantContext(options, tenantId))
        {
            version = await before.AcademicBatches.AsNoTracking().Where(x => x.Id == batchId)
                .Select(x => x.RowVersion).SingleAsync();
            Assert.Equal(8, version.Length);
        }
        await using (var db = OpenTenantContext(options, tenantId))
        {
            var denied = await Service(db, tenantId, managerUserId).EnrollAsync(secondRequest);
            Assert.Equal(409, denied.StatusCode);
        }
        await using (var check = OpenTenantContext(options, tenantId))
        {
            Assert.Single(await check.StudentEnrollments.AsNoTracking()
                .Where(x => x.AcademicBatchId == batchId && x.IsCurrent).ToListAsync());
            var afterVersion = await check.AcademicBatches.AsNoTracking().Where(x => x.Id == batchId)
                .Select(x => x.RowVersion).SingleAsync();
            Assert.Equal(version, afterVersion); // Failed allocation must not persist batch audit/version.
        }
    }

    private static EduOSDbContext OpenTenantContext(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private static AcademicEnrollmentService Service(EduOSDbContext db, long tenant, long managerUserId) => new(
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<StudentSubjectRegistration>(db),
        new GenericRepository<Student>(db), new GenericRepository<Guardian>(db),
        new GenericRepository<StudentGuardian>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<Subject>(db), new GenericRepository<SubjectPrerequisite>(db),
        new GenericRepository<RoutineEntry>(db),
        new GenericRepository<RoutineTimeSlot>(db), new GenericRepository<Room>(db),
        db, new TestManager(tenant, managerUserId), TimeProvider.System,
        NullLogger<AcademicEnrollmentService>.Instance);

    private sealed class TestManager(long tenantId, long managerUserId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => managerUserId;
        public long TenantId => tenantId;
        public string? FullName => "Enrollment SQL CI";
        public string? Email => "ci@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL CI";
    }
}
