using ClosedXML.Excel;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentImportExecutionServiceTests
{
    private static readonly Dictionary<string, string> Mapping = new()
    {
        ["SourceKey"] = "SourceKey", ["StudentCode"] = "StudentCode",
        ["FullName"] = "FullName", ["CampusId"] = "CampusId",
        ["AcademicBatchId"] = "AcademicBatchId", ["RollNo"] = "RollNo",
        ["GuardianName"] = "GuardianName", ["GuardianPhone"] = "GuardianPhone"
    };
    private const string Header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone\n";
    private static MemoryStream Csv(string value) => new(Encoding.UTF8.GetBytes(value));
    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-import-execute-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static async Task SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var db = Context(options, 101);
        db.Campuses.Add(new Campus { Id = 10, TenantId = 101, Name = "Main", Code = "MAIN" });
        db.AcademicBatches.Add(new AcademicBatch
        {
            Id = 20, TenantId = 101, CampusId = 10, Name = "Cohort", Code = "C1",
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1, Capacity = 100
        });
        db.AcademicCurriculums.Add(new AcademicCurriculum
        {
            Id = 50, TenantId = 101, AcademicProgramId = 1,
            Name = "Curriculum", Code = "CUR", IsActive = true, IsCurrent = true,
            EffectiveFrom = new DateOnly(2020, 1, 1)
        });
        await db.SaveChangesAsync();
    }
    private static StudentImportExecutionService Service(EduOSDbContext db, ICurrentUserService user,
        IUnitOfWork? unitOfWork = null, IStudentImportPreviewService? previewOverride = null)
    {
        var students = new GenericRepository<Student>(db);
        var enrollments = new GenericRepository<StudentEnrollment>(db);
        var batches = new GenericRepository<AcademicBatch>(db);
        var logs = new GenericRepository<ImportLog>(db);
        var items = new GenericRepository<ImportLogItem>(db);
        var preview = new StudentImportPreviewService(students, enrollments, batches,
            new GenericRepository<Campus>(db), logs, items, user,
            NullLogger<StudentImportPreviewService>.Instance);
        return new StudentImportExecutionService(previewOverride ?? preview, students, enrollments,
            new GenericRepository<StudentPersonLink>(db),
            new GenericRepository<Person>(db), new GenericRepository<Guardian>(db),
            new GenericRepository<StudentGuardian>(db), batches,
            new GenericRepository<AcademicCurriculum>(db),
            new GenericRepository<CurriculumSubject>(db),
            new GenericRepository<SubjectOffering>(db),
            new GenericRepository<StudentSubjectRegistration>(db), logs, items,
            new GenericRepository<IdempotencyRecord>(db), unitOfWork ?? new InMemoryUow(db),
            user, NullLogger<StudentImportExecutionService>.Instance);
    }


    [Fact]
    public async Task Required_subjects_are_approved_atomically_with_credit_snapshot_and_idempotent_replay()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        db.Subjects.Add(new Subject { Id = 60, TenantId = 101, Name = "Mathematics", Code = "MATH" });
        db.CurriculumSubjects.Add(new CurriculumSubject
        {
            Id = 61, TenantId = 101, AcademicCurriculumId = 50, AcademicLevelId = 1,
            SubjectId = 60, CreditHours = 4m, IsOptional = false
        });
        db.SubjectOfferings.Add(new SubjectOffering
        {
            Id = 62, TenantId = 101, AcademicBatchId = 20,
            AcademicYearId = 1, CurriculumSubjectId = 61, Code = "MATH-1", Capacity = 2
        });
        await db.SaveChangesAsync();
        var service = Service(db, new User(101, true));
        var requestId = Guid.NewGuid();
        var source = Header + "SUB-A,STU-A,Learner A,10,20,11,,\n" +
            "SUB-B,STU-B,Learner B,10,20,12,,\n";
        using (var file = Csv(source))
        {
            var result = await service.ExecuteAsync(file, ".csv", Mapping, requestId);
            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.Data!.ImportedRows);
        }
        var registrations = await db.StudentSubjectRegistrations.ToListAsync();
        Assert.Equal(2, registrations.Count);
        Assert.All(registrations, registration =>
        {
            Assert.Equal(62, registration.SubjectOfferingId);
            Assert.Equal(4m, registration.CreditHoursSnapshot);
            Assert.Equal(EduOS.Core.Enums.Domain.SubjectRegistrationState.Approved, registration.State);
            Assert.Equal(99L, registration.ApprovedByUserId);
        });
        Assert.Equal(2, registrations.Select(x => x.StudentEnrollmentId).Distinct().Count());
        using (var file = Csv(source))
        {
            var replay = await service.ExecuteAsync(file, ".csv", Mapping, requestId);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
        Assert.Equal(2, await db.StudentSubjectRegistrations.CountAsync());
    }

    [Fact]
    public async Task Missing_or_full_required_offering_rejects_entire_window_without_student_records()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        db.Subjects.Add(new Subject { Id = 60, TenantId = 101, Name = "Mathematics", Code = "MATH" });
        db.CurriculumSubjects.Add(new CurriculumSubject
        {
            Id = 61, TenantId = 101, AcademicCurriculumId = 50, AcademicLevelId = 1,
            SubjectId = 60, CreditHours = 4m, IsOptional = false
        });
        await db.SaveChangesAsync();
        var service = Service(db, new User(101, true));
        var source = Header + "SUB-C,STU-C,Learner C,10,20,11,,\n" +
            "SUB-D,STU-D,Learner D,10,20,12,,\n";
        using (var missing = Csv(source))
            Assert.Equal(409, (await service.ExecuteAsync(missing, ".csv", Mapping, Guid.NewGuid())).StatusCode);
        db.SubjectOfferings.Add(new SubjectOffering
        {
            Id = 62, TenantId = 101, AcademicBatchId = 20,
            AcademicYearId = 1, CurriculumSubjectId = 61, Code = "MATH-1", Capacity = 1
        });
        await db.SaveChangesAsync();
        using (var full = Csv(source))
            Assert.Equal(409, (await service.ExecuteAsync(full, ".csv", Mapping, Guid.NewGuid())).StatusCode);
        Assert.Empty(await db.Students.ToListAsync());
        Assert.Empty(await db.StudentEnrollments.ToListAsync());
        Assert.Empty(await db.StudentSubjectRegistrations.ToListAsync());
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public async Task Valid_batch_creates_private_student_guardian_enrollment_and_reconciliation()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var requestId = Guid.NewGuid();
        const string row = Header + "SOURCE-A,STU-A,Student One,10,20,11,Guardian One,01700000000\n";
        using var file = Csv(row);
        var result = await service.ExecuteAsync(file, ".csv", Mapping, requestId);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.ImportedRows);
        Assert.False(result.Data.Replayed);
        Assert.Single(await db.Students.ToListAsync());
        Assert.Single(await db.StudentEnrollments.ToListAsync());
        Assert.Single(await db.StudentPersonLinks.ToListAsync());
        Assert.Single(await db.Guardians.ToListAsync());
        Assert.Single(await db.StudentGuardians.ToListAsync());
        Assert.Equal(2, await db.Persons.CountAsync());
        Assert.Equal(1, await db.ImportLogItems.CountAsync());
        var reconciliation = await service.GetAsync(result.Data.ImportReference, 1, 50);
        Assert.True(reconciliation.Success);
        Assert.Equal("SOURCE-A", Assert.Single(reconciliation.Data!.Items).SourceKey);
        Assert.Equal(1, reconciliation.Data.SuccessRows);

        using var replayFile = Csv(row);
        var replay = await service.ExecuteAsync(replayFile, ".csv", Mapping, requestId);
        Assert.True(replay.Success);
        Assert.True(replay.Data!.Replayed);
        Assert.Single(await db.Students.ToListAsync());
    }

    [Fact]
    public async Task Replay_rejects_incomplete_committed_receipts_without_creating_more_learners()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var requestId = Guid.NewGuid();
        const string row = Header + "REPLAY-SRC,REPLAY-STU,Private Learner,10,20,11,,\n";
        using (var file = Csv(row))
            Assert.True((await service.ExecuteAsync(file, ".csv", Mapping, requestId)).Success);

        var log = await db.ImportLogs.SingleAsync();
        log.SuccessRows = 0;
        await db.SaveChangesAsync();
        using (var file = Csv(row))
            Assert.Equal(409, (await service.ExecuteAsync(file, ".csv", Mapping, requestId)).StatusCode);

        log.SuccessRows = 1;
        await db.SaveChangesAsync();
        var item = await db.ImportLogItems.SingleAsync();
        item.IsSuccess = false;
        await db.SaveChangesAsync();
        using (var file = Csv(row))
            Assert.Equal(409, (await service.ExecuteAsync(file, ".csv", Mapping, requestId)).StatusCode);

        Assert.Single(await db.Students.ToListAsync());
        Assert.Single(await db.StudentEnrollments.ToListAsync());
        Assert.Single(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public async Task Rejects_changed_request_and_duplicate_source_without_partial_writes()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var requestId = Guid.NewGuid();
        using (var first = Csv(Header + "SOURCE-A,STU-A,Student One,10,20,11,,\n"))
            Assert.True((await service.ExecuteAsync(first, ".csv", Mapping, requestId)).Success);
        using (var changed = Csv(Header + "SOURCE-B,STU-B,Student Two,10,20,12,,\n"))
            Assert.Equal(409, (await service.ExecuteAsync(changed, ".csv", Mapping, requestId)).StatusCode);
        using (var duplicate = Csv(Header + "SOURCE-A,STU-C,Student Three,10,20,13,,\n"))
            Assert.Equal(409, (await service.ExecuteAsync(duplicate, ".csv", Mapping, Guid.NewGuid())).StatusCode);
        Assert.Single(await db.Students.ToListAsync());
        Assert.Single(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public async Task Full_hundred_row_window_commits_once_and_rejects_source_reuse_without_partial_records()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var batch = await db.AcademicBatches.SingleAsync();
        batch.Capacity = 200;
        await db.SaveChangesAsync();
        var service = Service(db, new User(101, true));
        var request = Guid.NewGuid();
        var rows = Header + string.Concat(Enumerable.Range(1, 100)
            .Select(i => $"SRC-100-{i},STU-100-{i},Learner {i},10,20,{i},,\n"));

        using (var file = Csv(rows))
        {
            var result = await service.ExecuteAsync(file, ".csv", Mapping, request);
            Assert.True(result.Success, result.Message);
            Assert.Equal(100, result.Data!.ImportedRows);
        }
        using (var file = Csv(rows))
        {
            var replay = await service.ExecuteAsync(file, ".csv", Mapping, request);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
        using (var file = Csv(Header + "SRC-100-75,STU-NEW,New Learner,10,20,150,,\n"))
        {
            var conflict = await service.ExecuteAsync(file, ".csv", Mapping, Guid.NewGuid());
            Assert.Equal(409, conflict.StatusCode);
        }
        Assert.Equal(100, await db.Students.CountAsync());
        Assert.Equal(100, await db.Persons.CountAsync());
        Assert.Equal(100, await db.StudentPersonLinks.CountAsync());
        Assert.Equal(100, await db.StudentEnrollments.CountAsync());
        Assert.Equal(100, await db.ImportLogItems.CountAsync());
        Assert.Equal(100, await db.IdempotencyRecords.CountAsync(x =>
            x.Scope == "StudentImportSource" && x.TenantId == 101));
        Assert.Equal(1, await db.ImportLogs.CountAsync());
    }

    [Fact]
    public async Task Hundred_row_import_with_ten_shared_guardians_commits_in_bounded_save_phases()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var batch = await db.AcademicBatches.SingleAsync();
        batch.Capacity = 200;
        await db.SaveChangesAsync();
        var unitOfWork = new CountingUow(db);
        var service = Service(db, new User(101, true), unitOfWork);
        var mapping = new Dictionary<string, string>(Mapping)
        {
            ["GuardianSourceKey"] = "GuardianSourceKey"
        };
        const string header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        var source = header + string.Concat(Enumerable.Range(1, 100).Select(i =>
            $"PERF-SRC-{i},PERF-STU-{i},Learner {i},10,20,{i},Guardian {(i - 1) / 10},01700000000,FAMILY-{(i - 1) / 10}\n"));
        var request = Guid.NewGuid();

        using (var file = Csv(source))
        {
            var result = await service.ExecuteAsync(file, ".csv", mapping, request);
            Assert.True(result.Success, result.Message);
            Assert.Equal(100, result.Data!.ImportedRows);
        }
        // One batch lock, one import log, one Person phase, one Student/Guardian
        // phase and one dependent-record phase; no per-row database flushes.
        Assert.Equal(5, unitOfWork.SaveCount);
        Assert.Equal(100, await db.Students.CountAsync());
        Assert.Equal(110, await db.Persons.CountAsync());
        Assert.Equal(10, await db.Guardians.CountAsync());
        Assert.Equal(100, await db.StudentGuardians.CountAsync());
        Assert.Equal(100, await db.StudentPersonLinks.CountAsync());
        Assert.Equal(100, await db.StudentEnrollments.CountAsync());
        Assert.Equal(100, await db.ImportLogItems.CountAsync());
        Assert.Equal(100, await db.IdempotencyRecords.CountAsync(x => x.Scope == "StudentImportSource"));
        Assert.Equal(10, await db.IdempotencyRecords.CountAsync(x => x.Scope == "StudentImportGuardianSource"));
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.Scope == "StudentImportBatch"));
        Assert.Equal(10, await db.StudentGuardians.Select(x => x.GuardianId).Distinct().CountAsync());

        using (var file = Csv(source))
        {
            var replay = await service.ExecuteAsync(file, ".csv", mapping, request);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
        Assert.Equal(5, unitOfWork.SaveCount);
        Assert.Equal(100, await db.Students.CountAsync());
    }

    [Fact]
    public async Task Rejects_cross_tenant_batch_and_denies_unauthorized_reconciliation()
    {
        var options = Options(); await SeedAsync(options);
        await using var foreign = Context(options, 202);
        var service = Service(foreign, new User(202, true));
        using var file = Csv(Header + "SOURCE-X,STU-X,Student X,10,20,9,,\n");
        var result = await service.ExecuteAsync(file, ".csv", Mapping, Guid.NewGuid());
        Assert.Equal(409, result.StatusCode);
        Assert.Empty(await foreign.Students.ToListAsync());
        await using var db = Context(options, 101);
        var denied = Service(db, new User(101, false));
        using var another = Csv(Header + "SOURCE-Y,STU-Y,Student Y,10,20,9,,\n");
        Assert.Equal(403, (await denied.ExecuteAsync(another, ".csv", Mapping, Guid.NewGuid())).StatusCode);
        Assert.Equal(403, (await denied.GetAsync(Guid.NewGuid(), 1, 50)).StatusCode);
        Assert.Equal(400, (await Service(db, new User(101, true)).GetAsync(Guid.NewGuid(), 0, 50)).StatusCode);
    }

    [Fact]
    public async Task Source_file_windows_preserve_original_row_numbers_and_replay_safely()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var request = Guid.NewGuid();
        var rows = Header + string.Concat(Enumerable.Range(1, 101)
            .Select(i => $"SRC-{i},STU-{i},Learner {i},10,20,{i},,\n"));
        using (var file = Csv(rows))
        {
            var committed = await service.ExecuteSliceAsync(file, ".csv", Mapping, request, 100);
            Assert.True(committed.Success, committed.Message);
            Assert.Equal(1, committed.Data!.ImportedRows);
            Assert.Equal(102, Assert.Single(await db.ImportLogItems.ToListAsync()).RowNumber);
        }
        using (var file = Csv(rows))
        {
            var replay = await service.ExecuteSliceAsync(file, ".csv", Mapping, request, 100);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
        using (var file = Csv(rows.Replace("Learner 1,", "Changed learner 1,")))
            Assert.Equal(409, (await service.ExecuteSliceAsync(file, ".csv", Mapping, request, 100)).StatusCode);
        using (var file = Csv(rows))
        {
            Assert.Equal(400, (await service.ExecuteSliceAsync(file, ".csv", Mapping, Guid.NewGuid(), 1)).StatusCode);
            Assert.Equal(400, (await service.ExecuteSliceAsync(file, ".csv", Mapping, Guid.NewGuid(), 200)).StatusCode);
        }
        Assert.Single(await db.Students.ToListAsync());
    }

    [Fact]
    public async Task Rejects_unbounded_execution_before_any_mutation()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var rows = Header + string.Concat(Enumerable.Range(1, 101)
            .Select(i => $"SRC-{i},STU-{i},Learner {i},10,20,{i},,\n"));
        using var file = Csv(rows);
        Assert.Equal(400, (await Service(db, new User(101, true))
            .ExecuteAsync(file, ".csv", Mapping, Guid.NewGuid())).StatusCode);
        Assert.Empty(await db.Students.ToListAsync());
    }


    [Fact]
    public async Task Durable_job_status_recovers_committed_window_without_cross_tenant_disclosure()
    {
        var options = Options(); await SeedAsync(options);
        var rows = Header + string.Concat(Enumerable.Range(1, 101)
            .Select(i => $"SRC-{i},STU-{i},Learner {i},10,20,{i},,\n"));
        var fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rows))).ToLowerInvariant();
        var mappingHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Mapping)))).ToLowerInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(fileHash + ":" + mappingHash + ":100"));
        digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(digest.AsSpan(0, 16));
        var id = Guid.ParseExact(
            $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}", "D");
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        Assert.Equal(0, (await service.GetJobStatusAsync(fileHash, mappingHash, 101)).Data!.CommittedRows);
        using (var file = Csv(rows))
            Assert.True((await service.ExecuteSliceAsync(file, ".csv", Mapping, id, 100)).Success);
        var status = await service.GetJobStatusAsync(fileHash, mappingHash, 101);
        Assert.True(status.Success);
        Assert.Equal(1, status.Data!.CommittedRows);
        Assert.Equal(0, status.Data.NextOffset);
        Assert.False(status.Data.IsComplete);
        var batch = Assert.Single(status.Data.Batches);
        Assert.Equal(100, batch.RowOffset);
        Assert.True((await service.GetAsync(batch.ImportReference, 1, 100)).Success);
        Assert.Equal(400, (await service.GetJobStatusAsync("bad", mappingHash, 101)).StatusCode);
        Assert.Equal(400, (await service.GetJobStatusAsync(fileHash, mappingHash, 5001)).StatusCode);
        Assert.Equal(403, (await Service(db, new User(101, false))
            .GetJobStatusAsync(fileHash, mappingHash, 101)).StatusCode);
        await using var foreign = Context(options, 202);
        var isolated = await Service(foreign, new User(202, true))
            .GetJobStatusAsync(fileHash, mappingHash, 101);
        Assert.True(isolated.Success);
        Assert.Empty(isolated.Data!.Batches);
    }

    [Fact]
    public async Task Explicit_guardian_source_key_reuses_tenant_guardian_across_import_batches()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var mapped = new Dictionary<string, string>(Mapping) { ["GuardianSourceKey"] = "GuardianSourceKey" };
        const string header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        using (var first = Csv(header +
            "SRC-G1,STU-G1,Learner One,10,20,11,Guardian One,01700000000,FAMILY-1\n" +
            "SRC-G2,STU-G2,Learner Two,10,20,12,Guardian One,01700000000,FAMILY-1\n"))
            Assert.True((await service.ExecuteAsync(first, ".csv", mapped, Guid.NewGuid())).Success);
        Assert.Equal(2, await db.StudentGuardians.CountAsync());
        Assert.Single(await db.Guardians.ToListAsync());
        Assert.Equal(3, await db.Persons.CountAsync());

        using (var second = Csv(header +
            "SRC-G3,STU-G3,Learner Three,10,20,13,Guardian One,01700000000,FAMILY-1\n"))
            Assert.True((await service.ExecuteAsync(second, ".csv", mapped, Guid.NewGuid())).Success);
        Assert.Equal(3, await db.StudentGuardians.CountAsync());
        Assert.Single(await db.Guardians.ToListAsync());
        Assert.Equal(4, await db.Persons.CountAsync());

        using (var conflict = Csv(header +
            "SRC-G4,STU-G4,Learner Four,10,20,14,Guardian Different,01700000000,FAMILY-1\n"))
            Assert.Equal(409, (await service.ExecuteAsync(conflict, ".csv", mapped, Guid.NewGuid())).StatusCode);
        Assert.Equal(3, await db.Students.CountAsync());
        Assert.Equal(2, await db.ImportLogs.CountAsync());
        Assert.Single(await db.IdempotencyRecords.Where(x =>
            x.Scope == "StudentImportGuardianSource").ToListAsync());
    }

    [Fact]
    public async Task Guardian_without_explicit_source_key_is_never_automatically_reused()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using var file = Csv(Header +
            "SRC-N1,STU-N1,Learner One,10,20,11,Same Guardian,01700000000\n" +
            "SRC-N2,STU-N2,Learner Two,10,20,12,Same Guardian,01700000000\n");
        Assert.True((await service.ExecuteAsync(file, ".csv", Mapping, Guid.NewGuid())).Success);
        Assert.Equal(2, await db.Guardians.CountAsync());
    }


    [Fact]
    public async Task Cohort_reconciliation_aggregates_committed_and_missing_windows_without_cross_tenant_leak()
    {
        var options = Options(); await SeedAsync(options);
        const string first = Header + "COHORT-1,STU-C1,Student One,10,20,11,,\n";
        const string second = Header + "COHORT-2,STU-C2,Student Two,10,20,12,,\n";
        var fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first))).ToLowerInvariant();
        var mappingHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Mapping)))).ToLowerInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(fileHash + ":" + mappingHash + ":0"));
        digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(digest.AsSpan(0, 16));
        var requestId = Guid.ParseExact(
            $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}", "D");
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using (var stream = Csv(first))
            Assert.True((await service.ExecuteSliceAsync(stream, ".csv", Mapping, requestId, 0)).Success);
        var files = new[]
        {
            new EduOS.Core.DTOs.Students.StudentImportCohortFileRequestDto
                { FileHash = fileHash, MappingHash = mappingHash, TotalRows = 1 },
            new EduOS.Core.DTOs.Students.StudentImportCohortFileRequestDto
                { FileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(second))),
                    MappingHash = mappingHash, TotalRows = 1 }
        };
        var status = await service.GetCohortStatusAsync(files);
        Assert.True(status.Success, status.Message);
        Assert.Equal(2, status.Data!.ExpectedRows);
        Assert.Equal(1, status.Data.CommittedRows);
        Assert.Equal(1, status.Data.PendingRows);
        Assert.False(status.Data.IsComplete);
        Assert.Equal(0, status.Data.Files[0].MissingWindows);
        Assert.Single(status.Data.Files[0].CompletedWindows);
        Assert.Equal(0, status.Data.Files[0].CompletedWindows[0].RowOffset);
        Assert.Equal(1, status.Data.Files[0].CompletedWindows[0].ImportedRows);
        Assert.NotEqual(Guid.Empty, status.Data.Files[0].CompletedWindows[0].ImportReference);
        Assert.Equal(1, status.Data.Files[1].MissingWindows);
        Assert.Empty(status.Data.Files[1].CompletedWindows);
        await using var otherDb = Context(options, 202);
        var other = await Service(otherDb, new User(202, true)).GetCohortStatusAsync(files);
        Assert.True(other.Success);
        Assert.Equal(0, other.Data!.CommittedRows);
        Assert.All(other.Data.Files, x => Assert.Empty(x.CompletedWindows));
        Assert.Equal(403, (await Service(db, new User(101, false))
            .GetCohortStatusAsync(files)).StatusCode);
    }

    [Fact]
    public async Task Cohort_reconciliation_handles_400_windows_and_only_counts_fully_committed_tenant_logs()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var files = Enumerable.Range(0, 8).Select(i =>
            new EduOS.Core.DTOs.Students.StudentImportCohortFileRequestDto
            {
                FileHash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes("synthetic-5000-file-" + i))),
                MappingHash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes("synthetic-column-mapping"))),
                TotalRows = 5000
            }).ToArray();
        var now = DateTime.UtcNow;
        var logs = new List<ImportLog>();
        var keys = new List<IdempotencyRecord>();
        for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            var file = files[fileIndex];
            for (var offset = 0; offset < 5000; offset += 100)
            {
                var index = fileIndex * 50 + offset / 100;
                var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
                    file.FileHash.ToLowerInvariant() + ":" +
                    file.MappingHash.ToLowerInvariant() + ":" + offset));
                digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
                digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
                var hex = Convert.ToHexString(digest.AsSpan(0, 16));
                var requestId = Guid.ParseExact(
                    $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}", "D");
                var log = new ImportLog
                {
                    Id = 1000 + index, TenantId = 101, ImportType = "Student",
                    TotalRows = 100, SuccessRows = index == 399 ? 99 : 100,
                    PublicId = Guid.NewGuid(), CompletedAt = now, StartedAt = now
                };
                logs.Add(log);
                keys.Add(new IdempotencyRecord
                {
                    Id = 2000 + index, TenantId = 101, Scope = "StudentImportBatch",
                    RequestId = requestId, TargetId = log.Id,
                    CompletedAt = now, ExpiresAt = now.AddYears(1)
                });
            }
        }
        db.ImportLogs.AddRange(logs);
        db.IdempotencyRecords.AddRange(keys);
        await db.SaveChangesAsync();

        var service = Service(db, new User(101, true));
        var partial = await service.GetCohortStatusAsync(files);
        Assert.True(partial.Success, partial.Message);
        Assert.Equal(40000, partial.Data!.ExpectedRows);
        Assert.Equal(39900, partial.Data.CommittedRows);
        Assert.Equal(100, partial.Data.PendingRows);
        Assert.Equal(8, partial.Data.Files.Count);
        Assert.Equal(1, partial.Data.Files[7].MissingWindows);
        Assert.Equal(4900, partial.Data.Files[7].NextOffset);
        Assert.Equal(49, partial.Data.Files[7].CompletedWindows.Count);
        Assert.Equal(0, partial.Data.Files[7].CompletedWindows[0].RowOffset);
        Assert.Equal(4800, partial.Data.Files[7].CompletedWindows[^1].RowOffset);
        Assert.All(partial.Data.Files.Take(7), x =>
        {
            Assert.True(x.IsComplete);
            Assert.Equal(50, x.CompletedWindows.Count);
        });
        Assert.False(partial.Data.IsComplete);

        logs[399].SuccessRows = 100;
        await db.SaveChangesAsync();
        var recovered = await service.GetCohortStatusAsync(files);
        Assert.True(recovered.Success, recovered.Message);
        Assert.True(recovered.Data!.IsComplete);
        Assert.Equal(40000, recovered.Data.CommittedRows);
        Assert.All(recovered.Data.Files, x =>
        {
            Assert.Equal(5000, x.CommittedRows);
            Assert.Equal(5000, x.NextOffset);
            Assert.Equal(0, x.MissingWindows);
            Assert.Equal(50, x.CompletedWindows.Count);
            Assert.All(x.CompletedWindows, w => Assert.NotEqual(Guid.Empty, w.ImportReference));
        });
        await using var foreign = Context(options, 202);
        var isolated = await Service(foreign, new User(202, true)).GetCohortStatusAsync(files);
        Assert.True(isolated.Success);
        Assert.Equal(0, isolated.Data!.CommittedRows);
        Assert.Equal(40000, isolated.Data.PendingRows);
        Assert.All(isolated.Data.Files, x => Assert.Empty(x.CompletedWindows));
        Assert.Empty(await db.Students.ToListAsync());
    }

    [Fact]
    public async Task Cohort_reconciliation_rejects_duplicate_fingerprints_invalid_counts_and_unbounded_files()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var file = new EduOS.Core.DTOs.Students.StudentImportCohortFileRequestDto
        {
            FileHash = new string('a', 64), MappingHash = new string('b', 64), TotalRows = 5000
        };
        Assert.Equal(400, (await service.GetCohortStatusAsync(null)).StatusCode);
        Assert.Equal(400, (await service.GetCohortStatusAsync([file, file])).StatusCode);
        Assert.Equal(400, (await service.GetCohortStatusAsync(
            Enumerable.Repeat(file, 9).ToArray())).StatusCode);
        Assert.Equal(400, (await service.GetCohortStatusAsync([
            new EduOS.Core.DTOs.Students.StudentImportCohortFileRequestDto
                { FileHash = "bad", MappingHash = file.MappingHash, TotalRows = 1 }
        ])).StatusCode);
        Assert.Empty(await db.Students.ToListAsync());
    }


    [Fact]
    public async Task Correction_export_only_contains_uncommitted_windows_and_uses_safe_text_cells()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var source = Header + string.Concat(Enumerable.Range(1, 101)
            .Select(i => $"SRC-{i},STU-{i},{(i == 101 ? "=1+1" : "Learner " + i)},10,20,{i},,\n"));
        var mappingJson = JsonSerializer.Serialize(Mapping);
        var fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        var mappingHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mappingJson))).ToLowerInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(fileHash + ":" + mappingHash + ":0"));
        digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(digest.AsSpan(0, 16));
        var requestId = Guid.ParseExact(
            $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}", "D");
        using (var upload = Csv(source))
            Assert.True((await service.ExecuteSliceAsync(upload, ".csv", Mapping, requestId, 0)).Success);
        using var original = Csv(source);
        var export = await service.ExportPendingCorrectionsAsync(original, ".csv", Mapping, mappingJson);
        Assert.True(export.Success, export.Message);
        using var workbook = new XLWorkbook(new MemoryStream(export.Data!));
        var sheet = workbook.Worksheet(1);
        Assert.Equal("SourceRowNumber", sheet.Cell(1, 1).GetString());
        Assert.Equal(102, sheet.Cell(2, 1).GetValue<int>());
        Assert.Equal("SRC-101", sheet.Cell(2, 2).GetString());
        Assert.Equal("=1+1", sheet.Cell(2, 4).GetString());
        Assert.False(sheet.Cell(2, 4).HasFormula);
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(100, await db.Students.CountAsync());
    }

    [Fact]
    public async Task Correction_export_denies_unauthorized_and_invalid_inputs_without_writes()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var admin = Service(db, new User(101, true));
        var denied = Service(db, new User(101, false));
        var mappingJson = JsonSerializer.Serialize(Mapping);
        const string source = Header + "ONLY-1,STU-1,One,10,20,1,,\n";
        using (var file = Csv(source))
            Assert.Equal(403, (await denied.ExportPendingCorrectionsAsync(
                file, ".csv", Mapping, mappingJson)).StatusCode);
        using (var file = Csv(source))
            Assert.Equal(400, (await admin.ExportPendingCorrectionsAsync(
                file, ".txt", Mapping, mappingJson)).StatusCode);
        using (var file = Csv(source))
        {
            var result = await admin.ExportPendingCorrectionsAsync(file, ".csv", Mapping, mappingJson);
            Assert.True(result.Success, result.Message);
            using var workbook = new XLWorkbook(new MemoryStream(result.Data!));
            Assert.Equal(2, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
        }
        Assert.Empty(await db.Students.ToListAsync());
    }

    [Fact]
    public async Task Cohort_preflight_detects_cross_file_student_and_guardian_collisions_without_writes()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var mapping = new Dictionary<string, string>(Mapping) { ["GuardianSourceKey"] = "GuardianSourceKey" };
        const string header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        var files = new[]
        {
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(header +
                    "SRC-1,STU-1,Learner One,10,20,11,Guardian One,01700000000,FAMILY-1\n")
            },
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(header +
                    "src-1,stu-1,Learner Two,10,20,11,Guardian Different,01700000000,family-1\n")
            }
        };
        var report = await service.PreflightCohortAsync(files, mapping);
        Assert.True(report.Success, report.Message);
        Assert.Equal(2, report.Data!.TotalRows);
        Assert.Equal(4, report.Data.TotalIssues);
        Assert.All(report.Data.Issues, issue => Assert.Equal(2, issue.FileNumber));
        Assert.Contains(report.Data.Issues, x => x.Field == "GuardianSourceKey");
        Assert.Empty(await db.Students.ToListAsync());
        Assert.Empty(await db.Guardians.ToListAsync());
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public async Task Cohort_preflight_allows_consistent_guardian_reuse_but_denies_other_roles_and_invalid_files()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var mapping = new Dictionary<string, string>(Mapping) { ["GuardianSourceKey"] = "GuardianSourceKey" };
        const string header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        var files = new[]
        {
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(header +
                    "SRC-1,STU-1,Learner One,10,20,11,Guardian One,01700000000,FAMILY-1\n")
            },
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(header +
                    "SRC-2,STU-2,Learner Two,10,20,12,guardian one,01700000000,family-1\n")
            }
        };
        var report = await service.PreflightCohortAsync(files, mapping);
        Assert.True(report.Success, report.Message);
        Assert.Equal(0, report.Data!.TotalIssues);
        Assert.Equal(403, (await Service(db, new User(101, false))
            .PreflightCohortAsync(files, mapping)).StatusCode);
        Assert.Equal(400, (await service.PreflightCohortAsync([files[0]], mapping)).StatusCode);
        Assert.Equal(400, (await service.PreflightCohortAsync(
            Enumerable.Repeat(files[0], 9).ToArray(), mapping)).StatusCode);
        Assert.Equal(400, (await service.PreflightCohortAsync([
            files[0], new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
                { Extension = ".txt", Content = files[1].Content }
        ], mapping)).StatusCode);
        Assert.Empty(await db.Students.ToListAsync());
    }


    [Fact]
    public async Task Cohort_preflight_detects_cross_file_capacity_and_invalid_campus_without_writes()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var batch = await db.AcademicBatches.SingleAsync();
        batch.Capacity = 1;
        await db.SaveChangesAsync();
        var service = Service(db, new User(101, true));
        var files = new[]
        {
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    Header + "SRC-1,STU-1,Learner One,10,20,11,,\n")
            },
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    Header + "SRC-2,STU-2,Learner Two,10,20,12,,\n" +
                    "SRC-3,STU-3,Learner Three,999,20,13,,\n")
            }
        };
        var report = await service.PreflightCohortAsync(files, Mapping);
        Assert.True(report.Success, report.Message);
        Assert.Equal(3, report.Data!.TotalRows);
        Assert.Contains(report.Data.Issues, x => x.FileNumber == 2 && x.Field == "BatchCapacity");
        Assert.Contains(report.Data.Issues, x => x.FileNumber == 2 && x.Field == "BatchCampus");
        Assert.Empty(await db.Students.ToListAsync());
        Assert.Empty(await db.StudentEnrollments.ToListAsync());
    }

    [Fact]
    public async Task Cohort_preflight_checks_existing_tenant_guardian_key_without_exposing_identity()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var mapping = new Dictionary<string, string>(Mapping) { ["GuardianSourceKey"] = "GuardianSourceKey" };
        const string header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        using (var original = Csv(header + "ORIG,STU-ORIG,Learner,10,20,1,Guardian One,01700000000,FAMILY-1\n"))
            Assert.True((await service.ExecuteAsync(original, ".csv", mapping, Guid.NewGuid())).Success);
        var before = await db.Guardians.CountAsync();
        var files = new[]
        {
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    header + "SRC-1,STU-1,First,10,20,2,Guardian Different,01700000000,FAMILY-1\n")
            },
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    header + "SRC-2,STU-2,Second,10,20,3,Guardian Different,01700000000,FAMILY-1\n")
            }
        };
        var report = await service.PreflightCohortAsync(files, mapping);
        Assert.True(report.Success, report.Message);
        Assert.Equal(2, report.Data!.Issues.Count(x => x.Field == "GuardianSourceKey"));
        Assert.All(report.Data.Issues.Where(x => x.Field == "GuardianSourceKey"),
            issue => Assert.DoesNotContain("Guardian One", issue.Message));
        Assert.Equal(before, await db.Guardians.CountAsync());
        Assert.Single(await db.Students.ToListAsync());
    }

    [Fact]
    public async Task Cohort_preflight_detects_guardian_profile_drift_before_commit_without_identity_disclosure()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        var mapping = new Dictionary<string, string>(Mapping) { ["GuardianSourceKey"] = "GuardianSourceKey" };
        const string header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        using (var original = Csv(header + "OLD-1,OLD-STU,Original,10,20,1,Guardian One,01700000000,FAMILY-1\n"))
            Assert.True((await service.ExecuteAsync(original, ".csv", mapping, Guid.NewGuid())).Success);

        var guardian = await db.Guardians.SingleAsync();
        guardian.FullName = "Updated Private Guardian";
        await db.SaveChangesAsync();
        var files = new[]
        {
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    header + "NEW-1,NEW-STU-1,First,10,20,2,Guardian One,01700000000,FAMILY-1\n")
            },
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    header + "NEW-2,NEW-STU-2,Second,10,20,3,Guardian One,01700000000,FAMILY-1\n")
            }
        };
        var report = await service.PreflightCohortAsync(files, mapping);
        Assert.True(report.Success, report.Message);
        Assert.Equal(2, report.Data!.Issues.Count(x => x.Field == "GuardianSourceKey"));
        Assert.All(report.Data.Issues, issue =>
            Assert.DoesNotContain("Updated Private Guardian", issue.Message));

        using (var attempted = Csv(header + "NEW-3,NEW-STU-3,Third,10,20,4,Guardian One,01700000000,FAMILY-1\n"))
            Assert.Equal(409, (await service.ExecuteAsync(attempted, ".csv", mapping, Guid.NewGuid())).StatusCode);
        Assert.Single(await db.Guardians.ToListAsync());
        Assert.Single(await db.Students.ToListAsync());
        Assert.Single(await db.ImportLogs.ToListAsync());

        await using var foreign = Context(options, 202);
        var foreignReport = await Service(foreign, new User(202, true))
            .PreflightCohortAsync(files, mapping);
        Assert.True(foreignReport.Success);
        Assert.DoesNotContain(foreignReport.Data!.Issues, x => x.Field == "GuardianSourceKey");
    }

    [Fact]
    public async Task Cohort_preflight_reconciles_existing_tenant_source_keys_and_student_codes_without_disclosure()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using (var original = Csv(Header + "SOURCE-OLD,CODE-OLD,Existing Learner,10,20,1,,\n"))
            Assert.True((await service.ExecuteAsync(original, ".csv", Mapping, Guid.NewGuid())).Success);

        var files = new[]
        {
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    Header + "source-old,CODE-NEW,First,10,20,2,,\n")
            },
            new EduOS.Core.DTOs.Students.StudentImportCohortUploadDto
            {
                Extension = ".csv", Content = Encoding.UTF8.GetBytes(
                    Header + "SOURCE-NEW,CODE-OLD,Second,10,20,3,,\n")
            }
        };
        var report = await service.PreflightCohortAsync(files, Mapping);
        Assert.True(report.Success, report.Message);
        Assert.Contains(report.Data!.Issues, x => x.FileNumber == 1 &&
            x.Field == "SourceKey" && x.RowNumber == 2);
        Assert.Contains(report.Data.Issues, x => x.FileNumber == 2 &&
            x.Field == "StudentCode" && x.RowNumber == 2);
        Assert.DoesNotContain(report.Data.Issues, x => x.Message.Contains("Existing Learner"));
        Assert.Single(await db.Students.ToListAsync());
        Assert.Single(await db.ImportLogs.ToListAsync());

        await using var foreign = Context(options, 202);
        var isolated = await Service(foreign, new User(202, true)).PreflightCohortAsync(files, Mapping);
        Assert.True(isolated.Success, isolated.Message);
        Assert.DoesNotContain(isolated.Data!.Issues,
            x => x.Field == "SourceKey" || x.Field == "StudentCode");
    }

    [Fact]
    public async Task Committed_import_integrity_audit_detects_link_drift_without_disclosing_source_or_foreign_tenant()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using var csv = Csv(Header + "AUDIT-SOURCE,AUDIT-STU,Private Learner,10,20,7,,\n");
        var imported = await service.ExecuteAsync(csv, ".csv", Mapping, Guid.NewGuid());
        Assert.True(imported.Success, imported.Message);
        var reference = imported.Data!.ImportReference;
        var healthy = await service.GetIntegrityAsync(reference);
        Assert.True(healthy.Success, healthy.Message);
        Assert.True(healthy.Data!.IsHealthy);
        Assert.Equal(1, healthy.Data.HealthyRows);
        Assert.Empty(healthy.Data.Issues);

        var link = await db.StudentPersonLinks.SingleAsync();
        link.IsDeleted = true;
        await db.SaveChangesAsync();
        var drift = await service.GetIntegrityAsync(reference);
        Assert.True(drift.Success);
        Assert.False(drift.Data!.IsHealthy);
        Assert.Contains(drift.Data.Issues, x => x.Code == "PersonLinkMismatch" && x.RowNumber == 2);
        Assert.DoesNotContain("AUDIT-SOURCE", string.Join(" ", drift.Data.Issues.Select(x => x.Message)));
        Assert.DoesNotContain("Private Learner", string.Join(" ", drift.Data.Issues.Select(x => x.Message)));
        Assert.Single(await db.Students.ToListAsync());

        await using var foreign = Context(options, 202);
        Assert.Equal(404, (await Service(foreign, new User(202, true))
            .GetIntegrityAsync(reference)).StatusCode);
        Assert.Equal(403, (await Service(db, new User(101, false))
            .GetIntegrityAsync(reference)).StatusCode);
        Assert.Equal(400, (await service.GetIntegrityAsync(Guid.Empty)).StatusCode);
    }

    [Fact]
    public async Task Committed_integrity_audit_flags_duplicate_targets_and_deleted_person_without_private_details()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using var csv = Csv(Header +
            "CHECK-1,STU-CHECK-1,Private First,10,20,31,,\n" +
            "CHECK-2,STU-CHECK-2,Private Second,10,20,32,,\n");
        var imported = await service.ExecuteAsync(csv, ".csv", Mapping, Guid.NewGuid());
        Assert.True(imported.Success, imported.Message);
        var reference = imported.Data!.ImportReference;
        Assert.True((await service.GetIntegrityAsync(reference)).Data!.IsHealthy);

        var keys = await db.IdempotencyRecords.Where(x =>
            x.Scope == "StudentImportSource").OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, keys.Count);
        var originalTarget = keys[1].TargetId;
        keys[1].TargetId = keys[0].TargetId;
        await db.SaveChangesAsync();
        var duplicate = await service.GetIntegrityAsync(reference);
        Assert.True(duplicate.Success, duplicate.Message);
        Assert.False(duplicate.Data!.IsHealthy);
        Assert.Contains(duplicate.Data.Issues, x => x.Code == "DuplicateStudentTarget");
        Assert.DoesNotContain("Private", string.Join(" ", duplicate.Data.Issues.Select(x => x.Message)));
        Assert.DoesNotContain("CHECK-", string.Join(" ", duplicate.Data.Issues.Select(x => x.Message)));

        keys[1].TargetId = originalTarget;
        var student = await db.Students.SingleAsync(x => x.Id == originalTarget);
        var person = await db.Persons.SingleAsync(x => x.Id == student.PersonId);
        person.IsDeleted = true;
        await db.SaveChangesAsync();
        var missing = await service.GetIntegrityAsync(reference);
        Assert.True(missing.Success, missing.Message);
        Assert.False(missing.Data!.IsHealthy);
        Assert.Contains(missing.Data.Issues, x => x.Code == "MissingPersonIdentity");
        Assert.Equal(2, await db.Students.CountAsync());

        await using var foreign = Context(options, 202);
        Assert.Equal(404, (await Service(foreign, new User(202, true))
            .GetIntegrityAsync(reference)).StatusCode);
        Assert.Equal(403, (await Service(db, new User(101, false))
            .GetIntegrityAsync(reference)).StatusCode);
    }

    [Fact]
    public async Task Committed_integrity_audit_validates_optional_guardian_ownership_identity_and_primary_links()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using var csv = Csv(Header +
            "G-AUDIT-1,G-STU-1,Private Learner One,10,20,41,Private Guardian,01700000000\n" +
            "G-AUDIT-2,G-STU-2,Private Learner Two,10,20,42,,\n");
        var imported = await service.ExecuteAsync(csv, ".csv", Mapping, Guid.NewGuid());
        Assert.True(imported.Success, imported.Message);
        var reference = imported.Data!.ImportReference;
        var healthy = await service.GetIntegrityAsync(reference);
        Assert.True(healthy.Data!.IsHealthy);
        Assert.Equal(1, healthy.Data.GuardianLinksChecked);
        Assert.Equal(1, healthy.Data.GuardianLinksHealthy);

        var relation = await db.StudentGuardians.SingleAsync();
        var guardian = await db.Guardians.SingleAsync();
        guardian.IsDeleted = true;
        await db.SaveChangesAsync();
        var missing = await service.GetIntegrityAsync(reference);
        Assert.Contains(missing.Data!.Issues, x => x.Code == "MissingGuardian");
        Assert.Equal(0, missing.Data.GuardianLinksHealthy);

        guardian.IsDeleted = false;
        var person = await db.Persons.SingleAsync(x => x.Id == guardian.PersonId);
        person.IsDeleted = true;
        await db.SaveChangesAsync();
        var missingPerson = await service.GetIntegrityAsync(reference);
        Assert.Contains(missingPerson.Data!.Issues, x => x.Code == "MissingGuardianIdentity");

        person.IsDeleted = false;
        db.StudentGuardians.Add(new StudentGuardian
        {
            TenantId = 101, StudentId = relation.StudentId,
            GuardianId = guardian.Id, RelationCode = "Guardian", IsPrimary = true
        });
        await db.SaveChangesAsync();
        var duplicate = await service.GetIntegrityAsync(reference);
        Assert.Contains(duplicate.Data!.Issues, x => x.Code == "DuplicateGuardianLink");
        Assert.Contains(duplicate.Data.Issues, x => x.Code == "AmbiguousPrimaryGuardian");
        Assert.DoesNotContain("Private", string.Join(" ", duplicate.Data.Issues.Select(x => x.Message)));
        Assert.DoesNotContain("G-AUDIT", string.Join(" ", duplicate.Data.Issues.Select(x => x.Message)));

        await using var foreign = Context(options, 202);
        Assert.Equal(404, (await Service(foreign, new User(202, true))
            .GetIntegrityAsync(reference)).StatusCode);
    }

    [Fact]
    public async Task Committed_integrity_audit_rejects_historically_unlinked_primary_person_link()
    {
        var options = Options(); await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101, true));
        using var csv = Csv(Header + "UNLINK-SRC,UNLINK-STU,Private Learner,10,20,43,,\n");
        var imported = await service.ExecuteAsync(csv, ".csv", Mapping, Guid.NewGuid());
        Assert.True(imported.Success, imported.Message);
        var link = await db.StudentPersonLinks.SingleAsync();
        link.UnlinkedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var report = await service.GetIntegrityAsync(imported.Data!.ImportReference);
        Assert.False(report.Data!.IsHealthy);
        Assert.Contains(report.Data.Issues, x => x.Code == "PersonLinkMismatch");
    }

    [Fact]
    public async Task Transactional_preview_failure_uses_rollback_path_not_error_response_commit()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        var user = new User(101, true);
        var realPreview = new StudentImportPreviewService(
            new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
            new GenericRepository<AcademicBatch>(db), new GenericRepository<Campus>(db),
            new GenericRepository<ImportLog>(db), new GenericRepository<ImportLogItem>(db),
            user, NullLogger<StudentImportPreviewService>.Instance);
        var failingPreview = new FailOnFirstTransactionalPreview(realPreview);
        var transaction = new TransactionProbeUow(db);
        var service = Service(db, user, transaction, failingPreview);
        using var csv = Csv(Header + "ROLLBACK-SRC,ROLLBACK-STU,Student One,10,20,11,,\n");

        var result = await service.ExecuteAsync(csv, ".csv", Mapping, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(500, result.StatusCode);
        Assert.Equal(1, failingPreview.Calls);
        Assert.Equal(0, transaction.Commits);
        Assert.Equal(1, transaction.Rollbacks);
        Assert.Empty(await db.Students.ToListAsync());
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    [Fact]
    public async Task Transactional_preview_runs_once_after_batch_lock()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        var user = new User(101, true);
        var counting = new CountingUow(db);
        var realPreview = new StudentImportPreviewService(
            new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
            new GenericRepository<AcademicBatch>(db), new GenericRepository<Campus>(db),
            new GenericRepository<ImportLog>(db), new GenericRepository<ImportLogItem>(db),
            user, NullLogger<StudentImportPreviewService>.Instance);
        var preview = new LockOrderPreview(realPreview, () => counting.SaveCount);
        var service = Service(db, user, counting, preview);
        using var csv = Csv(Header + "LOCK-SRC,LOCK-STU,Student One,10,20,11,,\n");

        var result = await service.ExecuteAsync(csv, ".csv", Mapping, Guid.NewGuid());

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, preview.Calls);
        Assert.Single(await db.Students.ToListAsync());
        Assert.Single(await db.StudentEnrollments.ToListAsync());
    }

    [Fact]
    public async Task Invalid_post_lock_preview_aborts_transaction_callback()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = Context(options, 101);
        var user = new User(101, true);
        var transaction = new TransactionProbeUow(db);
        var preview = new RejectTransactionalPreview();
        var service = Service(db, user, transaction, preview);
        using var csv = Csv(Header + "REJECT-SRC,REJECT-STU,Student One,10,20,11,,\n");

        var result = await service.ExecuteAsync(csv, ".csv", Mapping, Guid.NewGuid());

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(1, preview.Calls);
        Assert.Equal(0, transaction.Commits);
        Assert.Equal(1, transaction.Rollbacks);
        Assert.Empty(await db.Students.ToListAsync());
        Assert.Empty(await db.ImportLogs.ToListAsync());
    }

    private sealed class LockOrderPreview(IStudentImportPreviewService inner, Func<int> saves)
        : IStudentImportPreviewService
    {
        public int Calls { get; private set; }
        public Task<ApiResponse<IReadOnlyList<string>>> GetHeadersAsync(Stream content, string extension,
            CancellationToken token = default) => inner.GetHeadersAsync(content, extension, token);
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewAsync(Stream content, string extension,
            IReadOnlyDictionary<string, string> mapping, CancellationToken token = default)
            => inner.PreviewAsync(content, extension, mapping, token);
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewAllErrorsAsync(Stream content, string extension,
            IReadOnlyDictionary<string, string> mapping, CancellationToken token = default)
            => inner.PreviewAllErrorsAsync(content, extension, mapping, token);
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewForExecutionAsync(Stream content, string extension,
            IReadOnlyDictionary<string, string> mapping, CancellationToken token = default)
        {
            Calls++;
            Assert.True(saves() > 0, "The batch rowversion lock must be saved before transactional preview.");
            return inner.PreviewForExecutionAsync(content, extension, mapping, token);
        }
    }

    private sealed class RejectTransactionalPreview : IStudentImportPreviewService
    {
        public int Calls { get; private set; }
        public Task<ApiResponse<IReadOnlyList<string>>> GetHeadersAsync(Stream content, string extension,
            CancellationToken token = default) => Task.FromResult(ApiResponse<IReadOnlyList<string>>.ErrorResponse("Denied.", 403));
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewAsync(Stream content, string extension,
            IReadOnlyDictionary<string, string> mapping, CancellationToken token = default)
            => Task.FromResult(ApiResponse<StudentImportPreviewDto>.ErrorResponse("Rejected.", 400));
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewAllErrorsAsync(Stream content, string extension,
            IReadOnlyDictionary<string, string> mapping, CancellationToken token = default)
            => PreviewAsync(content, extension, mapping, token);
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewForExecutionAsync(Stream content, string extension,
            IReadOnlyDictionary<string, string> mapping, CancellationToken token = default)
        {
            Calls++;
            return Task.FromResult(ApiResponse<StudentImportPreviewDto>.ErrorResponse("Rejected.", 400));
        }
    }

    private sealed class FailOnFirstTransactionalPreview(IStudentImportPreviewService inner)
        : IStudentImportPreviewService
    {
        public int Calls { get; private set; }
        public Task<ApiResponse<IReadOnlyList<string>>> GetHeadersAsync(
            Stream content, string extension, CancellationToken token = default)
            => inner.GetHeadersAsync(content, extension, token);
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewAsync(
            Stream content, string extension, IReadOnlyDictionary<string, string> mapping,
            CancellationToken token = default)
            => inner.PreviewAsync(content, extension, mapping, token);
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewAllErrorsAsync(
            Stream content, string extension, IReadOnlyDictionary<string, string> mapping,
            CancellationToken token = default)
            => inner.PreviewAllErrorsAsync(content, extension, mapping, token);
        public Task<ApiResponse<StudentImportPreviewDto>> PreviewForExecutionAsync(
            Stream content, string extension, IReadOnlyDictionary<string, string> mapping,
            CancellationToken token = default)
        {
            if (++Calls == 1)
                throw new InvalidOperationException("Synthetic transactional preview infrastructure failure.");
            return inner.PreviewForExecutionAsync(content, extension, mapping, token);
        }
    }

    private sealed class TransactionProbeUow(EduOSDbContext db) : IUnitOfWork
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken token = default) => db.SaveChangesAsync(token);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken token = default) => ExecuteInTransactionAsync(operation, IsolationLevel.Serializable, token);
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            IsolationLevel isolationLevel, CancellationToken token = default)
        {
            try
            {
                var result = await operation(token);
                Commits++;
                return result;
            }
            catch
            {
                Rollbacks++;
                throw;
            }
        }
    }

    private sealed class CountingUow(EduOSDbContext db) : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public async Task<int> SaveChangesAsync(CancellationToken token = default)
        {
            SaveCount++;
            return await db.SaveChangesAsync(token);
        }
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken token = default) => operation(token);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            IsolationLevel isolationLevel, CancellationToken token = default) => operation(token);
    }

    private sealed class InMemoryUow(EduOSDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken token = default) => db.SaveChangesAsync(token);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken token = default) => operation(token);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            IsolationLevel isolationLevel, CancellationToken token = default) => operation(token);
    }
    private sealed class User(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 99;
        public bool IsTenantAdmin => admin;
        public bool IsSuperAdmin => false;
        public string? FullName => null;
        public string? Email => null;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : [];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => "Test";
    }
}
