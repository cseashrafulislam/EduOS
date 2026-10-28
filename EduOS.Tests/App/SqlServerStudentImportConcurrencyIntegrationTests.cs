using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
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
using EduOS.Service.Services.Academic;
using EduOS.Service.Services.Admission;
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
        ["AcademicBatchId"] = "AcademicBatchId", ["RollNo"] = "RollNo",
        ["GuardianName"] = "GuardianName", ["GuardianPhone"] = "GuardianPhone",
        ["GuardianSourceKey"] = "GuardianSourceKey"
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
        var guardianNames = new[] { $"First Guardian {suffix}", $"Second Guardian {suffix}" };
        var header = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n";
        // Two separate tenant-authorized HTTP-style requests with independent DbContexts.
        var sources = new[]
        {
            header + $"SQL-SRC-A-{suffix},SQL-STU-A-{suffix},{learnerNames[0]},{await CampusIdAsync(options, tenantId, batchId)},{batchId},01,{guardianNames[0]},01700000000,SQL-FAM-A-{suffix}\n",
            header + $"SQL-SRC-B-{suffix},SQL-STU-B-{suffix},{learnerNames[1]},{await CampusIdAsync(options, tenantId, batchId)},{batchId},02,{guardianNames[1]},01700000001,SQL-FAM-B-{suffix}\n"
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
            // The losing transaction must also roll back its guardian Person,
            // tenant-owned Guardian, relationship and guardian source identity.
            var guardian = await verify.Guardians.AsNoTracking().SingleAsync();
            var guardianLink = await verify.StudentGuardians.AsNoTracking().SingleAsync();
            Assert.Equal(committedStudent.Id, guardianLink.StudentId);
            Assert.Equal(guardian.Id, guardianLink.GuardianId);
            Assert.Equal(guardianNames[winner], guardian.FullName);
            Assert.Equal(1, await verify.Persons.CountAsync(x =>
                x.FullName == guardianNames[0] || x.FullName == guardianNames[1]));
            Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportGuardianSource"));
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
        await using (var integrityDb = TenantDb(options, tenantId))
        {
            var audit = await Service(integrityDb, tenantId, adminId)
                .GetIntegrityAsync(results[winner].Data!.ImportReference);
            Assert.True(audit.Success, audit.Message);
            Assert.True(audit.Data!.IsHealthy);
            Assert.Equal(1, audit.Data.HealthyRows);
            Assert.Equal(1, audit.Data.GuardianLinksChecked);
            Assert.Equal(1, audit.Data.GuardianLinksHealthy);
            var guardian = await integrityDb.Guardians.SingleAsync();
            guardian.IsDeleted = true;
            await integrityDb.SaveChangesAsync();
            var drift = await Service(integrityDb, tenantId, adminId)
                .GetIntegrityAsync(results[winner].Data!.ImportReference);
            Assert.True(drift.Success, drift.Message);
            Assert.Contains(drift.Data!.Issues, x => x.Code == "MissingGuardian");
            guardian.IsDeleted = false;
            await integrityDb.SaveChangesAsync();
        }
        await using (var foreign = TenantDb(options, tenantId + 1000000))
        {
            var denied = await Service(foreign, tenantId + 1000000, adminId)
                .GetAsync(results[winner].Data!.ImportReference, 1, 20);
            Assert.Equal(404, denied.StatusCode);
            Assert.Equal(404, (await Service(foreign, tenantId + 1000000, adminId)
                .GetIntegrityAsync(results[winner].Data!.ImportReference)).StatusCode);
        }
    }


    [Fact]
    public async Task Concurrent_import_and_real_promotion_cannot_overfill_last_seat()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, adminId, campusId, targetBatchId;
        Guid studentReference;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-MIX-" + suffix, Name = "Student Allocation Race CI",
                Email = "mix-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "mix-admin-" + suffix + "@example.test";
            var admin = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Allocation SQL Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.AddRange(tenant, admin);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; adminId = admin.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var oldYear = new AcademicYear
                {
                    TenantId = tenantId, Name = "2025", Code = "2025",
                    StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31)
                };
                var newYear = new AcademicYear
                {
                    TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
                };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Training", Code = "TRAIN" };
                setup.AddRange(campus, oldYear, newYear, program);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var level = new AcademicLevel
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level One", Code = "L1", LevelNo = 1
                };
                var curriculum = new AcademicCurriculum
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Current Curriculum", Code = "CUR-26",
                    EffectiveFrom = new DateOnly(2025, 1, 1), IsCurrent = true, IsActive = true
                };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var oldBatch = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = oldYear.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Previous Year", Code = "OLD", Capacity = 2
                };
                var targetBatch = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = newYear.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Last Seat", Code = "TARGET", Capacity = 1
                };
                setup.AddRange(oldBatch, targetBatch);
                await setup.SaveChangesAsync();
                targetBatchId = targetBatch.Id;

                var person = new Person { FullName = "Existing Learner " + suffix };
                setup.Add(person);
                await setup.SaveChangesAsync();
                var student = new Student
                {
                    TenantId = tenantId, PersonId = person.Id,
                    FullName = person.FullName, StudentCode = "MIX-OLD-" + suffix,
                    StatusCode = "Active", AdmissionDate = new DateOnly(2025, 1, 10)
                };
                setup.Add(student);
                await setup.SaveChangesAsync();
                studentReference = student.PublicId;
                setup.Add(new StudentPersonLink
                {
                    TenantId = tenantId, StudentId = student.Id, PersonId = person.Id,
                    IsPrimary = true, LinkedAt = DateTime.UtcNow, LinkedByUserId = adminId
                });
                setup.Add(new StudentEnrollment
                {
                    TenantId = tenantId, StudentId = student.Id,
                    ClientRequestId = Guid.NewGuid(), CampusId = campus.Id,
                    AcademicYearId = oldYear.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicBatchId = oldBatch.Id,
                    AcademicCurriculumId = curriculum.Id, RollNo = "01",
                    EnrollmentDate = new DateOnly(2025, 1, 10),
                    State = EnrollmentState.Active, IsCurrent = true
                });
                await setup.SaveChangesAsync();
            }
        }

        EduOS.Core.DTOs.Students.StudentPromotionOptionsDto promotionOptions;
        await using (var db = TenantDb(options, tenantId))
        {
            var response = await Promotion(db, tenantId, adminId).GetOptionsAsync(studentReference);
            Assert.True(response.Success, response.Message);
            promotionOptions = response.Data!;
            Assert.Contains(promotionOptions.Targets, x => x.AcademicBatchId == targetBatchId);
        }

        var request = new EduOS.Core.DTOs.Students.PromoteStudentWorkflowRequestDto
        {
            ClientRequestId = Guid.NewGuid(),
            SourceEnrollmentId = promotionOptions.SourceEnrollmentId,
            TargetAcademicYearId = promotionOptions.Targets.Single(x => x.AcademicBatchId == targetBatchId).AcademicYearId,
            TargetAcademicLevelId = promotionOptions.Targets.Single(x => x.AcademicBatchId == targetBatchId).AcademicLevelId,
            TargetAcademicBatchId = targetBatchId,
            Decision = StudentProgressionDecisionType.Repeated,
            TargetRoll = "02", StudentRowVersion = promotionOptions.StudentRowVersion,
            SourceEnrollmentRowVersion = promotionOptions.SourceEnrollmentRowVersion
        };
        var csv = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n" +
            "MIX-SRC-" + suffix + ",MIX-NEW-" + suffix + ",Imported Learner " + suffix +
            "," + campusId + "," + targetBatchId + ",03,,,\n";
        var importId = Guid.NewGuid();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var promotionTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            await start.Task;
            return await Promotion(db, tenantId, adminId).PromoteAsync(studentReference, request);
        });
        var importTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            await start.Task;
            return await Service(db, tenantId, adminId).ExecuteAsync(stream, ".csv", Mapping, importId);
        });
        start.SetResult(true);
        await Task.WhenAll(promotionTask, importTask);
        var promotion = await promotionTask;
        var imported = await importTask;
        Assert.True(promotion.Success != imported.Success,
            "Exactly one last-seat allocation must commit. Promotion: " + promotion.StatusCode +
            " / import: " + imported.StatusCode);
        Assert.Equal(409, promotion.Success ? imported.StatusCode : promotion.StatusCode);

        await using (var verify = TenantDb(options, tenantId))
        {
            Assert.Equal(1, await verify.StudentEnrollments.CountAsync(x =>
                x.AcademicBatchId == targetBatchId && x.IsCurrent && x.State == EnrollmentState.Active));
            Assert.Equal(promotion.Success ? 1 : 0,
                await verify.StudentPromotionRecords.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0, await verify.ImportLogs.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0, await verify.Persons.CountAsync(x =>
                x.FullName == "Imported Learner " + suffix));
            Assert.Equal(imported.Success ? 2 : 1, await verify.Students.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportBatch" && x.RequestId == importId));
        }
        if (imported.Success)
        {
            await using var replayDb = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var replay = await Service(replayDb, tenantId, adminId)
                .ExecuteAsync(stream, ".csv", Mapping, importId);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
    }

    [Fact]
    public async Task Concurrent_import_and_real_readmission_cannot_overfill_last_seat()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var currentYear = DateTime.UtcNow.Year;
        long tenantId, adminId, campusId, targetBatchId;
        Guid studentReference;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-READMIT-" + suffix, Name = "Student Readmission Race CI",
                Email = "readmit-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "readmit-admin-" + suffix + "@example.test";
            var admin = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Readmission SQL Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.AddRange(tenant, admin);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; adminId = admin.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var oldYear = new AcademicYear
                {
                    TenantId = tenantId, Name = (currentYear - 1).ToString(),
                    Code = (currentYear - 1).ToString(),
                    StartDate = new DateOnly(currentYear - 1, 1, 1),
                    EndDate = new DateOnly(currentYear - 1, 12, 31)
                };
                var newYear = new AcademicYear
                {
                    TenantId = tenantId, Name = currentYear.ToString(), Code = currentYear.ToString(),
                    StartDate = new DateOnly(currentYear, 1, 1),
                    EndDate = new DateOnly(currentYear, 12, 31)
                };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Training", Code = "TRAIN" };
                setup.AddRange(campus, oldYear, newYear, program);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var level = new AcademicLevel
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level One", Code = "L1", LevelNo = 1
                };
                var curriculum = new AcademicCurriculum
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Current Curriculum", Code = "CUR",
                    EffectiveFrom = new DateOnly(currentYear - 1, 1, 1),
                    IsCurrent = true, IsActive = true
                };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var oldBatch = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = oldYear.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Previous Year", Code = "OLD", Capacity = 2
                };
                var targetBatch = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = newYear.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Last Seat", Code = "TARGET", Capacity = 1
                };
                setup.AddRange(oldBatch, targetBatch);
                await setup.SaveChangesAsync();
                targetBatchId = targetBatch.Id;
                var person = new Person { FullName = "Exited Learner " + suffix };
                setup.Add(person);
                await setup.SaveChangesAsync();
                var student = new Student
                {
                    TenantId = tenantId, PersonId = person.Id, FullName = person.FullName,
                    StudentCode = "READMIT-OLD-" + suffix, StatusCode = "Dropout",
                    AdmissionDate = new DateOnly(currentYear - 1, 1, 10)
                };
                setup.Add(student);
                await setup.SaveChangesAsync();
                studentReference = student.PublicId;
                var previous = new StudentEnrollment
                {
                    TenantId = tenantId, StudentId = student.Id, ClientRequestId = Guid.NewGuid(),
                    CampusId = campus.Id, AcademicYearId = oldYear.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    AcademicBatchId = oldBatch.Id, AcademicCurriculumId = curriculum.Id,
                    RollNo = "01", EnrollmentDate = new DateOnly(currentYear - 1, 1, 10),
                    EndDate = new DateOnly(currentYear - 1, 12, 1),
                    State = EnrollmentState.Dropped, IsCurrent = false
                };
                setup.Add(previous);
                setup.Add(new StudentPersonLink
                {
                    TenantId = tenantId, StudentId = student.Id, PersonId = person.Id,
                    IsPrimary = true, LinkedAt = DateTime.UtcNow, LinkedByUserId = adminId
                });
                await setup.SaveChangesAsync();
                setup.Add(new StudentExitRecord
                {
                    TenantId = tenantId, StudentId = student.Id, StudentEnrollmentId = previous.Id,
                    ClientRequestId = Guid.NewGuid(), ExitType = StudentExitType.Dropout,
                    DueAtExit = 0, FeesCleared = true, Reason = "Synthetic final dropout",
                    ProcessedAt = DateTime.UtcNow, ProcessedByUserId = adminId
                });
                await setup.SaveChangesAsync();
            }
        }

        StudentReadmissionOptionsDto readmissionOptions;
        await using (var db = TenantDb(options, tenantId))
        {
            var response = await Readmission(db, tenantId, adminId).GetOptionsAsync(studentReference);
            Assert.True(response.Success, response.Message);
            readmissionOptions = response.Data!;
            Assert.Contains(readmissionOptions.Targets, x => x.AcademicBatchId == targetBatchId);
        }
        var request = new ProcessStudentReadmissionDto
        {
            ClientRequestId = Guid.NewGuid(),
            PreviousEnrollmentId = readmissionOptions.PreviousEnrollmentId,
            TargetAcademicBatchId = targetBatchId, TargetRoll = "02",
            Reason = "Approved synthetic return after dropout",
            StudentRowVersion = readmissionOptions.StudentRowVersion,
            PreviousEnrollmentRowVersion = readmissionOptions.PreviousEnrollmentRowVersion
        };
        var csv = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n" +
            "READMIT-SRC-" + suffix + ",READMIT-NEW-" + suffix + ",Imported Learner " + suffix +
            "," + campusId + "," + targetBatchId + ",03,,,\n";
        var importId = Guid.NewGuid();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readmissionTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            await start.Task;
            return await Readmission(db, tenantId, adminId).ProcessAsync(studentReference, request);
        });
        var importTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            await start.Task;
            return await Service(db, tenantId, adminId).ExecuteAsync(stream, ".csv", Mapping, importId);
        });
        start.SetResult(true);
        await Task.WhenAll(readmissionTask, importTask);
        var readmission = await readmissionTask;
        var imported = await importTask;
        Assert.True(readmission.Success != imported.Success,
            "Exactly one last-seat allocation must commit. Readmission: " +
            readmission.StatusCode + " / import: " + imported.StatusCode);
        Assert.Equal(409, readmission.Success ? imported.StatusCode : readmission.StatusCode);

        await using (var verify = TenantDb(options, tenantId))
        {
            Assert.Equal(1, await verify.StudentEnrollments.CountAsync(x =>
                x.AcademicBatchId == targetBatchId && x.IsCurrent && x.State == EnrollmentState.Active));
            Assert.Equal(1, await verify.StudentExitRecords.CountAsync());
            Assert.Equal(1, await verify.StudentEnrollments.CountAsync(x => !x.IsCurrent));
            Assert.Equal(readmission.Success ? 1 : 0,
                await verify.StudentStatusHistories.CountAsync(x => x.ToStatusCode == "Active"));
            Assert.Equal(imported.Success ? 1 : 0, await verify.ImportLogs.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0,
                await verify.Persons.CountAsync(x => x.FullName == "Imported Learner " + suffix));
            Assert.Equal(imported.Success ? 2 : 1, await verify.Students.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportBatch" && x.RequestId == importId));
            Assert.Equal(readmission.Success ? "Active" : "Dropout",
                await verify.Students.Where(x => x.PublicId == studentReference)
                    .Select(x => x.StatusCode).SingleAsync());
        }
        if (readmission.Success)
        {
            await using var db = TenantDb(options, tenantId);
            var replay = await Readmission(db, tenantId, adminId).ProcessAsync(studentReference, request);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.AlreadyProcessed);
        }
        else
        {
            await using var db = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var replay = await Service(db, tenantId, adminId).ExecuteAsync(stream, ".csv", Mapping, importId);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
        await using var foreign = TenantDb(options, tenantId + 1000000);
        Assert.Equal(404, (await Readmission(foreign, tenantId + 1000000, adminId)
            .GetOptionsAsync(studentReference)).StatusCode);
    }


    [Fact]
    public async Task Concurrent_import_and_real_academic_enrollment_cannot_overfill_last_seat()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long tenantId, adminId, campusId, batchId, curriculumId;
        Guid studentReference;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-ENROLL-" + suffix, Name = "Academic Enrollment Import Race CI",
                Email = "enroll-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "enroll-admin-" + suffix + "@example.test";
            var admin = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Enrollment SQL Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.AddRange(tenant, admin);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            adminId = admin.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear
                {
                    TenantId = tenantId, Name = today.Year.ToString(), Code = today.Year.ToString(),
                    StartDate = new DateOnly(today.Year, 1, 1),
                    EndDate = new DateOnly(today.Year, 12, 31), IsCurrent = true
                };
                var program = new AcademicProgram
                {
                    TenantId = tenantId, Name = "Training", Code = "TRAIN"
                };
                setup.AddRange(campus, year, program);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var level = new AcademicLevel
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level One", Code = "L1", LevelNo = 1
                };
                var curriculum = new AcademicCurriculum
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Current Curriculum", Code = "CUR",
                    EffectiveFrom = new DateOnly(today.Year, 1, 1),
                    IsCurrent = true, IsActive = true
                };
                var subject = new Subject { TenantId = tenantId, Code = "MATH", Name = "Mathematics" };
                setup.AddRange(level, curriculum, subject);
                await setup.SaveChangesAsync();
                curriculumId = curriculum.Id;
                var batch = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Last Seat", Code = "TARGET", Capacity = 1
                };
                var required = new CurriculumSubject
                {
                    TenantId = tenantId, AcademicCurriculumId = curriculum.Id,
                    AcademicLevelId = level.Id, SubjectId = subject.Id,
                    CreditHours = 4m, IsOptional = false
                };
                setup.AddRange(batch, required);
                await setup.SaveChangesAsync();
                batchId = batch.Id;
                setup.Add(new SubjectOffering
                {
                    TenantId = tenantId, AcademicBatchId = batch.Id,
                    CurriculumSubjectId = required.Id, AcademicYearId = year.Id,
                    Code = "MATH-1", Capacity = 1
                });
                var person = new Person { FullName = "Existing Learner " + suffix };
                setup.Add(person);
                await setup.SaveChangesAsync();
                var student = new Student
                {
                    TenantId = tenantId, PersonId = person.Id,
                    FullName = person.FullName, StudentCode = "ENROLL-OLD-" + suffix,
                    StatusCode = "Active", AdmissionDate = today
                };
                setup.Add(student);
                await setup.SaveChangesAsync();
                studentReference = student.PublicId;
                setup.Add(new StudentPersonLink
                {
                    TenantId = tenantId, StudentId = student.Id, PersonId = person.Id,
                    IsPrimary = true, LinkedAt = DateTime.UtcNow, LinkedByUserId = adminId
                });
                await setup.SaveChangesAsync();
            }
        }

        var enrollmentId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var enrollRequest = new CreateStudentEnrollmentRequestDto
        {
            ClientRequestId = enrollmentId, StudentReference = studentReference,
            AcademicBatchId = batchId, AcademicCurriculumId = curriculumId,
            RollNo = "01", EnrollmentDate = today
        };
        var csv = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n" +
            "ENROLL-SRC-" + suffix + ",ENROLL-NEW-" + suffix + ",Imported Learner " + suffix +
            "," + campusId + "," + batchId + ",02,,,\n";
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enrollmentTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            await start.Task;
            return await AcademicEnrollment(db, tenantId, adminId).EnrollAsync(enrollRequest);
        });
        var importTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            await start.Task;
            return await Service(db, tenantId, adminId).ExecuteAsync(stream, ".csv", Mapping, importId);
        });
        start.SetResult(true);
        await Task.WhenAll(enrollmentTask, importTask);
        var enrolled = await enrollmentTask;
        var imported = await importTask;
        Assert.True(enrolled.Success != imported.Success,
            "Exactly one last-seat allocation must commit. Academic enrollment: " +
            enrolled.StatusCode + " / import: " + imported.StatusCode);
        Assert.Equal(409, enrolled.Success ? imported.StatusCode : enrolled.StatusCode);

        await using (var verify = TenantDb(options, tenantId))
        {
            Assert.Equal(1, await verify.StudentEnrollments.CountAsync(x =>
                x.AcademicBatchId == batchId && x.IsCurrent && x.State == EnrollmentState.Active));
            Assert.Equal(imported.Success ? 1 : 0, await verify.ImportLogs.CountAsync());
            Assert.Equal(imported.Success ? 2 : 1, await verify.Students.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0, await verify.Persons.CountAsync(x =>
                x.FullName == "Imported Learner " + suffix));
            Assert.Equal(imported.Success ? 1 : 0, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportBatch" && x.RequestId == importId));
            Assert.Equal(enrolled.Success ? 1 : 0, await verify.StudentSubjectRegistrations.CountAsync());
        }
        if (imported.Success)
        {
            await using var db = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var replay = await Service(db, tenantId, adminId).ExecuteAsync(stream, ".csv", Mapping, importId);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
        else
        {
            await using var db = TenantDb(options, tenantId);
            var replay = await AcademicEnrollment(db, tenantId, adminId).EnrollAsync(enrollRequest);
            Assert.True(replay.Success, replay.Message);
        }
        await using var foreign = TenantDb(options, tenantId + 1000000);
        Assert.Equal(404, (await AcademicEnrollment(foreign, tenantId + 1000000, adminId)
            .GetCurrentAsync(studentReference)).StatusCode);
    }

    private static AcademicEnrollmentService AcademicEnrollment(
        EduOSDbContext db, long tenant, long userId) => new(
            new GenericRepository<StudentEnrollment>(db),
            new GenericRepository<StudentSubjectRegistration>(db),
            new GenericRepository<Student>(db), new GenericRepository<Guardian>(db),
            new GenericRepository<StudentGuardian>(db), new GenericRepository<AcademicBatch>(db),
            new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicCurriculum>(db),
            new GenericRepository<CurriculumSubject>(db), new GenericRepository<SubjectOffering>(db),
            new GenericRepository<Subject>(db), new GenericRepository<SubjectPrerequisite>(db),
            new GenericRepository<RoutineEntry>(db), new GenericRepository<RoutineTimeSlot>(db),
            new GenericRepository<Room>(db), db, new TestAdmin(tenant, userId),
            TimeProvider.System, NullLogger<AcademicEnrollmentService>.Instance);


    [Fact]
    public async Task Concurrent_import_and_real_admission_conversion_cannot_overfill_last_seat()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long tenantId, adminId, campusId, batchId;
        Guid applicationReference;
        string applicationVersion;

        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant
            {
                Code = "CI-ADMIT-" + suffix, Name = "Admission Import Race CI",
                Email = "admit-" + suffix + "@example.test", State = TenantState.Active
            };
            var username = "admit-admin-" + suffix + "@example.test";
            var admin = new ApplicationUser
            {
                UserName = username, NormalizedUserName = username.ToUpperInvariant(),
                Email = username, NormalizedEmail = username.ToUpperInvariant(),
                FullName = "Admission SQL Admin", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            setup.AddRange(tenant, admin);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            adminId = admin.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear
                {
                    TenantId = tenantId, Name = today.Year.ToString(), Code = today.Year.ToString(),
                    StartDate = new DateOnly(today.Year, 1, 1),
                    EndDate = new DateOnly(today.Year, 12, 31), IsCurrent = true
                };
                var program = new AcademicProgram
                {
                    TenantId = tenantId, Name = "Training", Code = "TRAIN"
                };
                setup.AddRange(campus, year, program);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var level = new AcademicLevel
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level One", Code = "L1", LevelNo = 1
                };
                var curriculum = new AcademicCurriculum
                {
                    TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Current Curriculum", Code = "CUR",
                    EffectiveFrom = new DateOnly(today.Year, 1, 1),
                    IsCurrent = true, IsActive = true
                };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Last Seat", Code = "TARGET", Capacity = 1
                };
                var form = new AdmissionIntakeForm
                {
                    TenantId = tenantId, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Title = "Admissions", Code = "INTAKE", State = AdmissionFormState.Published
                };
                setup.AddRange(batch, form);
                await setup.SaveChangesAsync();
                batchId = batch.Id;
                var application = new AdmissionApplicant
                {
                    TenantId = tenantId, AdmissionIntakeFormId = form.Id,
                    ClientRequestId = Guid.NewGuid(), ApplicationNumber = "APP-" + suffix,
                    FullName = "Accepted Applicant " + suffix, State = AdmissionApplicantState.Qualified
                };
                setup.Add(application);
                await setup.SaveChangesAsync();
                applicationReference = application.PublicId;
                applicationVersion = Convert.ToBase64String(application.RowVersion);
                setup.Add(new AdmissionDecision
                {
                    TenantId = tenantId, AdmissionApplicantId = application.Id,
                    ClientRequestId = Guid.NewGuid(), OfferedAcademicBatchId = batch.Id,
                    State = AdmissionDecisionState.Accepted, AcceptedAt = DateTime.UtcNow
                });
                await setup.SaveChangesAsync();
            }
        }

        var admitRequest = new AdmitAdmissionApplicationDto
        {
            AcademicBatchId = batchId, Roll = "01", RowVersion = applicationVersion
        };
        var importId = Guid.NewGuid();
        var csv = "SourceKey,StudentCode,FullName,CampusId,AcademicBatchId,RollNo,GuardianName,GuardianPhone,GuardianSourceKey\n" +
            "ADMIT-SRC-" + suffix + ",ADMIT-NEW-" + suffix + ",Imported Learner " + suffix +
            "," + campusId + "," + batchId + ",02,,,\n";
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            await start.Task;
            return await Admission(db, tenantId, adminId).AdmitAsync(applicationReference, admitRequest);
        });
        var importTask = Task.Run(async () =>
        {
            await using var db = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            await start.Task;
            return await Service(db, tenantId, adminId).ExecuteAsync(stream, ".csv", Mapping, importId);
        });
        start.SetResult(true);
        await Task.WhenAll(admitTask, importTask);
        var admitted = await admitTask;
        var imported = await importTask;
        Assert.True(admitted.Success != imported.Success,
            "Exactly one last-seat allocation must commit. Admission: " +
            admitted.StatusCode + " / import: " + imported.StatusCode);
        Assert.Equal(409, admitted.Success ? imported.StatusCode : admitted.StatusCode);

        await using (var verify = TenantDb(options, tenantId))
        {
            Assert.Equal(1, await verify.StudentEnrollments.CountAsync(x =>
                x.AcademicBatchId == batchId && x.IsCurrent && x.State == EnrollmentState.Active));
            Assert.Equal(1, await verify.Students.CountAsync());
            Assert.Equal(1, await verify.StudentPersonLinks.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0, await verify.ImportLogs.CountAsync());
            Assert.Equal(imported.Success ? 1 : 0, await verify.IdempotencyRecords.CountAsync(x =>
                x.TenantId == tenantId && x.Scope == "StudentImportBatch" && x.RequestId == importId));
            var application = await verify.AdmissionApplicants.SingleAsync(x => x.PublicId == applicationReference);
            Assert.Equal(admitted.Success ? AdmissionApplicantState.Admitted : AdmissionApplicantState.Qualified,
                application.State);
            Assert.Equal(admitted.Success, application.ConvertedStudentId.HasValue);
            Assert.Equal(admitted.Success, application.ConvertedEnrollmentId.HasValue);
        }
        if (imported.Success)
        {
            await using var db = TenantDb(options, tenantId);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var replay = await Service(db, tenantId, adminId).ExecuteAsync(stream, ".csv", Mapping, importId);
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.Replayed);
        }
        else
        {
            await using var db = TenantDb(options, tenantId);
            var replay = await Admission(db, tenantId, adminId).AdmitAsync(applicationReference, admitRequest);
            Assert.True(replay.Success, replay.Message);
        }
        await using var foreign = TenantDb(options, tenantId + 1000000);
        Assert.Equal(404, (await Admission(foreign, tenantId + 1000000, adminId)
            .GetOptionsAsync(applicationReference)).StatusCode);
    }

    private static AdmissionEnrollmentService Admission(
        EduOSDbContext db, long tenant, long userId) => new(
            new GenericRepository<AdmissionApplicant>(db),
            new GenericRepository<AdmissionIntakeForm>(db),
            new GenericRepository<AdmissionDecision>(db),
            new GenericRepository<AdmissionApplicantDocument>(db),
            new GenericRepository<AdmissionApplicantGuardian>(db),
            new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
            new GenericRepository<Guardian>(db), new GenericRepository<StudentGuardian>(db),
            new GenericRepository<Person>(db), new GenericRepository<StudentPersonLink>(db),
            new GenericRepository<AcademicBatch>(db), new GenericRepository<AcademicTrack>(db),
            new GenericRepository<AcademicCurriculum>(db), new GenericRepository<AcademicYear>(db),
            db, new TestAdmin(tenant, userId), TimeProvider.System,
            NullLogger<AdmissionEnrollmentService>.Instance);

    private static StudentReadmissionService Readmission(EduOSDbContext db, long tenant, long userId)
        => new(new GenericRepository<Student>(db),
            new GenericRepository<StudentEnrollment>(db),
            new GenericRepository<StudentExitRecord>(db),
            new GenericRepository<StudentStatusHistory>(db),
            new GenericRepository<TransferRequest>(db),
            new GenericRepository<AcademicYear>(db),
            new GenericRepository<AcademicLevel>(db),
            new GenericRepository<AcademicBatch>(db),
            new GenericRepository<AcademicTerm>(db),
            new GenericRepository<AcademicCurriculum>(db),
            db, new TestAdmin(tenant, userId), TimeProvider.System,
            NullLogger<StudentReadmissionService>.Instance);

    private static StudentPromotionService Promotion(EduOSDbContext db, long tenant, long userId)
        => new(new GenericRepository<Student>(db),
            new GenericRepository<StudentEnrollment>(db),
            new GenericRepository<StudentPromotionRecord>(db),
            new GenericRepository<AcademicYear>(db),
            new GenericRepository<AcademicLevel>(db),
            new GenericRepository<AcademicBatch>(db),
            new GenericRepository<AcademicCurriculum>(db),
            db, new TestAdmin(tenant, userId), TimeProvider.System,
            NullLogger<StudentPromotionService>.Instance);

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
