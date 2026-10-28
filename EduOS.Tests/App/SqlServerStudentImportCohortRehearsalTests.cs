using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace EduOS.Tests.App;

/// <summary>
/// Exercises the real tenant-scoped, 100-row transactional import and database-backed
/// cohort recovery on disposable SQL Server. The 40K scenario is explicitly opt-in:
/// never run against a production connection or with real learner records.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerStudentImportCohortRehearsalTests
{
    private readonly ITestOutputHelper _output;

    public SqlServerStudentImportCohortRehearsalTests(ITestOutputHelper output) => _output = output;

    private const string ConnectionVariable = "EDUOS_SQL_AUTH_TEST_CONNECTION";
    private const string FullRehearsalVariable = "EDUOS_SQL_IMPORT_40K_REHEARSAL";
    private static readonly Dictionary<string, string> Mapping = new()
    {
        ["SourceKey"] = "SourceKey", ["StudentCode"] = "StudentCode",
        ["FullName"] = "FullName", ["CampusId"] = "CampusId",
        ["AcademicBatchId"] = "AcademicBatchId", ["RollNo"] = "RollNo",
        ["GuardianName"] = "GuardianName", ["GuardianPhone"] = "GuardianPhone",
        ["GuardianSourceKey"] = "GuardianSourceKey"
    };

    [Fact]
    public async Task Sql_recovery_smoke_reconciles_partial_cohort_and_denies_foreign_tenant()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection)) return;
        await RunScenarioAsync(connection, fileCount: 2, rowsPerFile: 200);
    }

    [Fact]
    public async Task Sql_parallel_institute_imports_reconcile_independently_with_shared_source_keys()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection)) return;
        // Both institutes deliberately use the same SourceKey and GuardianSourceKey
        // values, but independent tenant-owned students, guardians and enrollments.
        // Distinct DbContexts execute real transactions simultaneously.
        await Task.WhenAll(
            RunScenarioAsync(connection, fileCount: 2, rowsPerFile: 200),
            RunScenarioAsync(connection, fileCount: 2, rowsPerFile: 200));
    }

    [Fact]
    public async Task Opt_in_disposable_sql_rehearsal_imports_and_recovers_40000_synthetic_students()
    {
        if (Environment.GetEnvironmentVariable(FullRehearsalVariable) != "1") return;
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.False(string.IsNullOrWhiteSpace(connection),
            "The 40K rehearsal requires an explicitly configured disposable SQL connection.");
        // Fail closed: this destructive-volume synthetic test is permitted only
        // on the loopback disposable SQL database created by the smoke workflow.
        var target = new SqlConnectionStringBuilder(connection);
        Assert.True(string.Equals(target.DataSource, "127.0.0.1,14333",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(target.InitialCatalog, "EduOS_MigrationSmoke",
                StringComparison.OrdinalIgnoreCase),
            "40K rehearsal refused: only disposable loopback SQL Server is allowed.");
        await RunScenarioAsync(connection!, fileCount: 8, rowsPerFile: 5000);
    }

    private async Task RunScenarioAsync(string connection, int fileCount, int rowsPerFile)
    {
        var total = checked(fileCount * rowsPerFile);
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var (tenant, admin, campus, batch) = await SeedAsync(options, suffix, total);
        var mappingHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Mapping)));
        var sourceFiles = Enumerable.Range(0, fileCount)
            .Select(i => BuildCsv(suffix, i, rowsPerFile, campus, batch)).ToArray();
        var manifest = sourceFiles.Select(bytes => new StudentImportCohortFileRequestDto
        {
            FileHash = Hash(bytes), MappingHash = mappingHash, TotalRows = rowsPerFile
        }).ToList();
        var jobs = Enumerable.Range(0, fileCount)
            .SelectMany(file => Enumerable.Range(0, rowsPerFile / 100)
                .Select(window => (File: file, Offset: window * 100,
                    Request: RequestId(manifest[file].FileHash, mappingHash, window * 100))))
            .ToArray();
        var durations = new List<double>(jobs.Length);
        Guid firstReference = Guid.Empty;
        var beforeSql = await SqlCountersAsync(connection);
        var clock = Stopwatch.StartNew();

        // Alternating windows leave intentional gaps in every source file. Each
        // subsequent window uses a fresh DbContext, as after a worker restart.
        for (var phase = 0; phase < 2; phase++)
        {
            foreach (var job in jobs.Where(x => (x.Offset / 100) % 2 == phase))
            {
                await using var db = TenantDb(options, tenant);
                var service = Service(db, tenant, admin);
                using var source = new MemoryStream(sourceFiles[job.File], writable: false);
                var start = Stopwatch.GetTimestamp();
                var result = await service.ExecuteSliceAsync(source, ".csv", Mapping,
                    job.Request, job.Offset);
                durations.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                Assert.True(result.Success,
                    $"Synthetic file {job.File + 1}, window {job.Offset}: {result.StatusCode} {result.Message}");
                Assert.Equal(100, result.Data!.ImportedRows);
                Assert.False(result.Data.Replayed);
                if (firstReference == Guid.Empty) firstReference = result.Data.ImportReference;
            }

            await using var checkDb = TenantDb(options, tenant);
            var check = await Service(checkDb, tenant, admin).GetCohortStatusAsync(manifest);
            Assert.True(check.Success, check.Message);
            Assert.Equal(total, check.Data!.ExpectedRows);
            Assert.Equal(phase == 0 ? total / 2 : total, check.Data.CommittedRows);
            Assert.Equal(phase == 0 ? total / 2 : 0, check.Data.PendingRows);
            Assert.Equal(phase == 1, check.Data.IsComplete);
            Assert.Equal(fileCount, check.Data.Files.Count);
            Assert.All(check.Data.Files, file =>
            {
                Assert.Equal(phase == 0 ? rowsPerFile / 2 : rowsPerFile, file.CommittedRows);
                Assert.Equal(phase == 0 ? rowsPerFile / 200 : 0, file.MissingWindows);
                Assert.Equal(phase == 0 ? rowsPerFile / 200 : rowsPerFile / 100,
                    file.CompletedWindows.Count);
                Assert.All(file.CompletedWindows, window =>
                {
                    Assert.NotEqual(Guid.Empty, window.ImportReference);
                    Assert.Equal(100, window.ImportedRows);
                });
            });
        }

        // Replaying after a process/context restart must not create new records.
        foreach (var job in new[] { jobs[0], jobs[^1] })
        {
            await using var db = TenantDb(options, tenant);
            using var source = new MemoryStream(sourceFiles[job.File], writable: false);
            var replay = await Service(db, tenant, admin).ExecuteSliceAsync(
                source, ".csv", Mapping, job.Request, job.Offset);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }

        await using (var auditDb = TenantDb(options, tenant))
        {
            var status = await Service(auditDb, tenant, admin).GetCohortStatusAsync(manifest);
            Assert.True(status.Success, status.Message);
            var first = status.Data!.Files[0].CompletedWindows[0];
            var last = status.Data.Files[^1].CompletedWindows[^1];
            foreach (var window in new[] { first, last })
            {
                var audit = await Service(auditDb, tenant, admin)
                    .GetIntegrityAsync(window.ImportReference);
                Assert.True(audit.Success, audit.Message);
                Assert.True(audit.Data!.IsHealthy);
                Assert.Equal(100, audit.Data.HealthyRows);
            }
        }

        await using (var verify = TenantDb(options, tenant))
        {
            Assert.Equal(total, await verify.Students.CountAsync());
            Assert.Equal(total, await verify.StudentEnrollments.CountAsync());
            Assert.Equal(total, await verify.StudentPersonLinks.CountAsync());
            Assert.Equal(total, await verify.StudentGuardians.CountAsync());
            Assert.Equal(total / 100, await verify.Guardians.CountAsync());
            Assert.Equal(total + total / 100, await verify.Persons.CountAsync(x =>
                x.FullName.Contains(suffix)));
            Assert.Equal(jobs.Length, await verify.ImportLogs.CountAsync());
            Assert.Equal(total, await verify.ImportLogItems.CountAsync());
            Assert.Equal(total, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenant && x.Scope == "StudentImportSource"));
            Assert.Equal(total / 100, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenant && x.Scope == "StudentImportGuardianSource"));
            Assert.Equal(jobs.Length, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenant && x.Scope == "StudentImportBatch"));
        }
        await using (var foreign = TenantDb(options, tenant + 1000000))
        {
            var service = Service(foreign, tenant + 1000000, admin);
            var status = await service.GetCohortStatusAsync(manifest);
            Assert.True(status.Success, status.Message);
            Assert.Equal(0, status.Data!.CommittedRows);
            Assert.Equal(total, status.Data.PendingRows);
            Assert.Equal(404, (await service.GetAsync(firstReference, 1, 20)).StatusCode);
        }

        clock.Stop();
        var afterSql = await SqlCountersAsync(connection);
        long Delta(int index) => Math.Max(0, afterSql[index] - beforeSql[index]);
        durations.Sort();
        static double Percentile(List<double> values, double p) =>
            values[(int)Math.Ceiling((values.Count - 1) * p)];
        // Aggregate timing only; no raw student, guardian, source key or connection data.
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "StudentImportSyntheticSql",
            Rows = total, Files = fileCount, Windows = jobs.Length,
            Seconds = Math.Round(clock.Elapsed.TotalSeconds, 2),
            RowsPerSecond = Math.Round(total / clock.Elapsed.TotalSeconds, 2),
            WindowLatencyMsP50 = Math.Round(Percentile(durations, .50), 2),
            WindowLatencyMsP95 = Math.Round(Percentile(durations, .95), 2),
            WindowLatencyMsP99 = Math.Round(Percentile(durations, .99), 2),
            Committed = total, ReplayedWindows = 2, CrossTenantReadback = "Denied",
            FailedWindows = 0,
            SqlCpuMs = Math.Round(Delta(0) / 1000d, 2),
            SqlLogicalReads = Delta(1), SqlPhysicalReads = Delta(2),
            SqlLogicalWrites = Delta(3),
            SqlFileReads = Delta(4), SqlFileWrites = Delta(5),
            SqlReadStallMs = Delta(6), SqlWriteStallMs = Delta(7),
            SqlLockWaits = Delta(8), SqlLockWaitMs = Delta(9),
            SqlDeadlocks = Delta(10),
            PeakTestProcessMemoryMb = Math.Round(Process.GetCurrentProcess().PeakWorkingSet64 / (1024d * 1024), 1)
        }));
    }


    // Aggregate SQL Server DMV deltas from this isolated disposable instance.
    // Query-cache and server-wide counters are indicative, not a concurrency benchmark.
    private static async Task<long[]> SqlCountersAsync(string connection)
    {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COALESCE(SUM(CONVERT(bigint, total_worker_time)), 0) FROM sys.dm_exec_query_stats),
              (SELECT COALESCE(SUM(CONVERT(bigint, total_logical_reads)), 0) FROM sys.dm_exec_query_stats),
              (SELECT COALESCE(SUM(CONVERT(bigint, total_physical_reads)), 0) FROM sys.dm_exec_query_stats),
              (SELECT COALESCE(SUM(CONVERT(bigint, total_logical_writes)), 0) FROM sys.dm_exec_query_stats),
              (SELECT COALESCE(SUM(CONVERT(bigint, num_of_reads)), 0) FROM sys.dm_io_virtual_file_stats(DB_ID(), NULL)),
              (SELECT COALESCE(SUM(CONVERT(bigint, num_of_writes)), 0) FROM sys.dm_io_virtual_file_stats(DB_ID(), NULL)),
              (SELECT COALESCE(SUM(CONVERT(bigint, io_stall_read_ms)), 0) FROM sys.dm_io_virtual_file_stats(DB_ID(), NULL)),
              (SELECT COALESCE(SUM(CONVERT(bigint, io_stall_write_ms)), 0) FROM sys.dm_io_virtual_file_stats(DB_ID(), NULL)),
              (SELECT COALESCE(SUM(CONVERT(bigint, waiting_tasks_count)), 0) FROM sys.dm_os_wait_stats WHERE wait_type LIKE 'LCK_M_%'),
              (SELECT COALESCE(SUM(CONVERT(bigint, wait_time_ms)), 0) FROM sys.dm_os_wait_stats WHERE wait_type LIKE 'LCK_M_%'),
              (SELECT COALESCE(SUM(CONVERT(bigint, cntr_value)), 0) FROM sys.dm_os_performance_counters WHERE counter_name LIKE 'Number of Deadlocks/sec%' AND instance_name = '_Total')
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "SQL resource counters unavailable.");
        return Enumerable.Range(0, 11).Select(reader.GetInt64).ToArray();
    }

    private static byte[] BuildCsv(string suffix, int file, int count, long campus, long batch)
    {
        var csv = new StringBuilder(
            "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n");
        for (var row = 0; row < count; row++)
        {
            var global = file * count + row + 1;
            var family = (global - 1) / 100;
            csv.Append("REH-SRC-").Append(global)
                .Append(",REH-STU-").Append(suffix).Append('-').Append(global)
                .Append(",Learner ").Append(suffix).Append(' ').Append(global)
                .Append(',').Append(campus).Append(',').Append(batch).Append(',').Append(global)
                .Append(",Guardian ").Append(suffix).Append(' ').Append(family)
                .Append(",01700000000,REH-FAM-").Append(family)
                .Append('\n');
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static Guid RequestId(string fileHash, string mappingHash, int offset)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            fileHash + ":" + mappingHash + ":" + offset));
        bytes[6] = (byte)((bytes[6] & 15) | 0x50);
        bytes[8] = (byte)((bytes[8] & 63) | 0x80);
        var hex = Convert.ToHexString(bytes.AsSpan(0, 16));
        return Guid.ParseExact(
            $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}", "D");
    }

    private static async Task<(long Tenant, long Admin, long Campus, long Batch)> SeedAsync(
        DbContextOptions<EduOSDbContext> options, string suffix, int capacity)
    {
        await using var db = new EduOSDbContext(options);
        var tenant = new Tenant
        {
            Code = "CI-REH-" + suffix, Name = "Synthetic Student Import Rehearsal",
            Email = "reh-" + suffix + "@example.test", State = TenantState.Active
        };
        var username = "reh-admin-" + suffix + "@example.test";
        var admin = new ApplicationUser
        {
            UserName = username, NormalizedUserName = username.ToUpperInvariant(),
            Email = username, NormalizedEmail = username.ToUpperInvariant(),
            FullName = "Synthetic Import Admin", EmailConfirmed = true, IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        db.AddRange(tenant, admin);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        long campusId, batchId;
        using (db.BeginSystemTenantScope(tenant.Id))
        {
            var campus = new Campus { TenantId = tenant.Id, Name = "Main", Code = "MAIN" };
            var year = new AcademicYear
            {
                TenantId = tenant.Id, Name = "2026", Code = "2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
            };
            var program = new AcademicProgram
            {
                TenantId = tenant.Id, Name = "Training", Code = "TRAIN"
            };
            db.AddRange(campus, year, program);
            await db.SaveChangesAsync();
            var level = new AcademicLevel
            {
                TenantId = tenant.Id, AcademicProgramId = program.Id,
                Name = "Level One", Code = "L1", LevelNo = 1
            };
            var curriculum = new AcademicCurriculum
            {
                TenantId = tenant.Id, AcademicProgramId = program.Id,
                Name = "2026 Curriculum", Code = "CUR-26",
                EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true, IsActive = true
            };
            db.AddRange(level, curriculum);
            await db.SaveChangesAsync();
            var batch = new AcademicBatch
            {
                TenantId = tenant.Id, CampusId = campus.Id, AcademicYearId = year.Id,
                AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                Name = "Synthetic Cohort", Code = "COHORT", Capacity = capacity
            };
            db.Add(batch);
            await db.SaveChangesAsync();
            campusId = campus.Id; batchId = batch.Id;
        }
        return (tenant.Id, admin.Id, campusId, batchId);
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private static StudentImportExecutionService Service(EduOSDbContext db, long tenant, long userId)
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
            NullLogger<StudentImportExecutionService>.Instance);
    }

    private sealed class TestAdmin(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => "Synthetic Import Admin";
        public string? Email => "synthetic@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL Import Rehearsal";
    }
}
