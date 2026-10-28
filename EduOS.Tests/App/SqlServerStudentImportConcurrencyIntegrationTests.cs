using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server race: two different source keys compete for the last seat.
/// Synthetic records only; the test is skipped without the SQL integration connection.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerStudentImportConcurrencyIntegrationTests
{
    private static readonly Dictionary<string, string> Mapping = new()
    {
        ["SourceKey"] = "SourceKey", ["StudentCode"] = "StudentCode",
        ["FullName"] = "FullName", ["CampusId"] = "CampusId",
        ["AcademicBatchId"] = "AcademicBatchId", ["RollNo"] = "RollNo"
    };

    [Fact]
    public async Task Concurrent_imports_for_last_seat_commit_once_and_replay_without_partial_records()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, batchId, adminId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-IMPORT-" + suffix, Name = "Student Import Race CI",
                Email = "import-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "import-admin-" + suffix + "@example.test";
            var admin = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Student Import SQL Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.AddRange(tenant, admin);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; adminId = admin.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear
                {
                    TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
                };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Training", Code = "TRAIN" };
                setup.AddRange(campus, year, program);
                await setup.SaveChangesAsync();
                var level = new AcademicLevel
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level One", Code = "L1", LevelNo = 1
                };
                var curriculum = new AcademicCurriculum
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "2026 Curriculum", Code = "CUR-26",
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true, IsActive = true
                };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "One Seat", Code = "SEAT-1", Capacity = 1
                };
                setup.Add(batch);
                await setup.SaveChangesAsync();
                batchId = batch.Id;
            }
        }

        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        // Globally unique synthetic names make orphan detection independent of
        // audit stamping and of identities seeded by other SQL smoke tests.
        var learnerNames = new[] { $"First Learner {suffix}", $"Second Learner {suffix}" };
        var header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo\n";
        // Two separate tenant-authorized HTTP-style requests with independent DbContexts.
        var sources = new[]
        {
            header + $"SQL-SRC-A-{suffix},SQL-STU-A-{suffix},{learnerNames[0]},{await CampusIdAsync(options, tenantId, batchId)},{batchId},01\n",
            header + $"SQL-SRC-B-{suffix},SQL-STU-B-{suffix},{learnerNames[1]},{await CampusIdAsync(options, tenantId, batchId)},{batchId},02\n"
        };
        var diagnostic = new System.Collections.Concurrent.ConcurrentBag<string>();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            await using var db = TenantDb(options, tenantId);
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(sources[i]));
            return await Service(db, tenantId, adminId, new ExceptionTypeLogger(diagnostic)).ExecuteAsync(source, ".csv", Mapping, ids[i]);
        }));

        Assert.Single(results, x => x.Success);
        Assert.True(results.Count(x => x.StatusCode == 409) == 1,
            string.Join("; ", results.Select(x => $"{x.StatusCode}: {x.Message}")) +
            " | exception types: " + string.Join(" | ", diagnostic));
        var winner = Array.FindIndex(results, x => x.Success);
        await using (var retryDb = TenantDb(options, tenantId))
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(sources[winner]));
            var replay = await Service(retryDb, tenantId, adminId)
                .ExecuteAsync(source, ".csv", Mapping, ids[winner]);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
            Assert.Equal(results[winner].Data!.ImportReference, replay.Data.ImportReference);
        }
        // A deadlock victim must not leave a request key or partial learner;
        // a fresh attempt still loses to the committed enrollment.
        var loser = 1 - winner;
        await using (var loserDb = TenantDb(options, tenantId))
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(sources[loser]));
            var retry = await Service(loserDb, tenantId, adminId)
                .ExecuteAsync(source, ".csv", Mapping, ids[loser]);
            Assert.Equal(409, retry.StatusCode);
        }
        await using (var verify = TenantDb(options, tenantId))
        {
            Assert.Equal(1, await verify.Students.CountAsync());
            Assert.Equal("SQL-STU-" + (winner == 0 ? "A" : "B") + "-" + suffix,
                await verify.Students.Select(x => x.StudentCode).SingleAsync());
            // Verify the committed learner through the tenant-owned link, then
            // independently detect an orphan Person left by the losing transaction.
            // Person is platform-global; CreatedBy may be audit-stamped by EF.
            var committedStudent = await verify.Students.AsNoTracking().SingleAsync();
            var committedLink = await verify.StudentPersonLinks.AsNoTracking().SingleAsync();
            Assert.Equal(committedStudent.Id, committedLink.StudentId);
            Assert.Equal(committedStudent.PersonId, committedLink.PersonId);
            Assert.Equal(learnerNames[winner], await verify.Persons.AsNoTracking()
                .Where(x => x.Id == committedLink.PersonId)
                .Select(x => x.FullName).SingleAsync());
            Assert.Equal(1, await verify.Persons.CountAsync(x =>
                x.FullName == learnerNames[0] || x.FullName == learnerNames[1]));
            Assert.Equal(0, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportBatch" && x.RequestId == ids[loser]));
            Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportBatch"));
            Assert.Equal(1, await verify.StudentEnrollments.CountAsync());
            Assert.Equal(1, await verify.StudentPersonLinks.CountAsync());
            Assert.Equal(1, await verify.ImportLogs.CountAsync());
            Assert.Equal(1, await verify.ImportLogItems.CountAsync());
            Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportSource"));
        }
        await using (var foreign = TenantDb(options, tenantId + 1000000))
        {
            var denied = await Service(foreign, tenantId + 1000000, adminId)
                .GetAsync(results[winner].Data!.ImportReference, 1, 20);
            Assert.Equal(404, denied.StatusCode);
        }
    }

    private static async Task<long> CampusIdAsync(
        DbContextOptions<EduOSDbContext> options, long tenant, long batch)
    {
        await using var db = TenantDb(options, tenant);
        return await db.AcademicBatches.Where(x => x.Id == batch)
            .Select(x => x.CampusId).SingleAsync();
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private static StudentImportExecutionService Service(EduOSDbContext db, long tenant, long userId,
        ILogger<StudentImportExecutionService>? logger = null)
    {
        var students = new GenericRepository<Student>(db);
        var enrollments = new GenericRepository<StudentEnrollment>(db);
        var batches = new GenericRepository<AcademicBatch>(db);
        var logs = new GenericRepository<ImportLog>(db);
        var items = new GenericRepository<ImportLogItem>(db);
        var admin = new TestAdmin(tenant, userId);
        var preview = new StudentImportPreviewService(students, enrollments, batches,
            new GenericRepository<Campus>(db), logs, items, admin,
            NullLogger<StudentImportPreviewService>.Instance);
        return new StudentImportExecutionService(preview, students, enrollments,
            new GenericRepository<StudentPersonLink>(db),
            new GenericRepository<Person>(db), new GenericRepository<Guardian>(db),
            new GenericRepository<StudentGuardian>(db), batches,
            new GenericRepository<AcademicCurriculum>(db), logs, items,
            new GenericRepository<IdempotencyRecord>(db), db, admin,
            logger ?? NullLogger<StudentImportExecutionService>.Instance);
    }

    // Synthetic SQL integration only: emit exception types and SQL numbers, never learner data.
    private sealed class ExceptionTypeLogger(System.Collections.Concurrent.ConcurrentBag<string> output)
        : ILogger<StudentImportExecutionService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) where TState : notnull
        {
            if (exception == null) return;
            var chain = new List<string>();
            for (var current = exception; current != null; current = current.InnerException)
                chain.Add(current.GetType().Name +
                    (current is Microsoft.Data.SqlClient.SqlException sql ? "#" + sql.Number : ""));
            output.Add(string.Join(" -> ", chain));
        }
    }

    private sealed class TestAdmin(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => "SQL Import Admin";
        public string? Email => "import@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL Import CI";
    }
}
