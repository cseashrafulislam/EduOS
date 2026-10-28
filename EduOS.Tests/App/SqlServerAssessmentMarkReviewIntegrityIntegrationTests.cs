using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Real SQL Server assertions for durable examination review evidence.
/// Uses isolated synthetic tenants and never changes customer records.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerAssessmentMarkReviewIntegrityIntegrationTests
{
    [Fact]
    public async Task Review_events_are_tenant_isolated_unique_append_only_and_fk_scoped()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantA, tenantB, subjectA, subjectB, actorId;
        var key = Guid.NewGuid();

        await using (var setup = new EduOSDbContext(options))
        {
            var a = new Tenant { Code = "CI-EXA-" + suffix, Name = "Exam review A",
                Email = "exama-" + suffix + "@example.test", State = TenantState.Active };
            var b = new Tenant { Code = "CI-EXB-" + suffix, Name = "Exam review B",
                Email = "examb-" + suffix + "@example.test", State = TenantState.Active };
            var name = "exam-review-" + suffix + "@example.test";
            var actor = new ApplicationUser { UserName = name,
                NormalizedUserName = name.ToUpperInvariant(), Email = name,
                NormalizedEmail = name.ToUpperInvariant(), FullName = "Synthetic reviewer",
                IsActive = true, EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
            setup.AddRange(a, b, actor);
            await setup.SaveChangesAsync();
            tenantA = a.Id; tenantB = b.Id; actorId = actor.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantA))
                subjectA = await SeedSubjectAsync(setup, tenantA, suffix + "A");
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantB))
                subjectB = await SeedSubjectAsync(setup, tenantB, suffix + "B");
        }

        var first = new AssessmentMarkReviewEvent {
            TenantId = tenantA, AssessmentSubjectId = subjectA, ActorUserId = actorId,
            ClientRequestId = key, Decision = AssessmentMarkReviewDecision.Submitted,
            MarkRowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
            OccurredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        };
        await using (var db = TenantDb(options, tenantA))
        {
            db.AssessmentMarkReviewEvents.Add(first);
            await db.SaveChangesAsync();
            Assert.NotEmpty(first.RowVersion);
            Assert.Equal(1, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.TenantId == tenantA && x.AssessmentSubjectId == subjectA));
            Assert.False(await db.AssessmentMarkReviewEvents.AsNoTracking()
                .AnyAsync(x => x.AssessmentSubjectId == subjectB));
        }
        // The same idempotency key may be used independently in another institute.
        await using (var db = TenantDb(options, tenantB))
        {
            db.AssessmentMarkReviewEvents.Add(new AssessmentMarkReviewEvent {
                TenantId = tenantB, AssessmentSubjectId = subjectB, ActorUserId = actorId,
                ClientRequestId = key, Decision = AssessmentMarkReviewDecision.Submitted,
                MarkRowVersion = first.MarkRowVersion,
                OccurredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.AssessmentMarkReviewEvents.AsNoTracking().CountAsync());
            Assert.False(await db.AssessmentMarkReviewEvents.AsNoTracking()
                .AnyAsync(x => x.Id == first.Id));
        }
        // The database, not only the service, must reject duplicate request keys.
        await using (var db = TenantDb(options, tenantA))
        {
            db.AssessmentMarkReviewEvents.Add(new AssessmentMarkReviewEvent {
                TenantId = tenantA, AssessmentSubjectId = subjectA, ActorUserId = actorId,
                ClientRequestId = key, Decision = AssessmentMarkReviewDecision.Approved,
                MarkRowVersion = first.MarkRowVersion,
                OccurredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        // Tenant-composite FK must reject a forged subject ID from another institute.
        await using (var db = TenantDb(options, tenantA))
        {
            db.AssessmentMarkReviewEvents.Add(new AssessmentMarkReviewEvent {
                TenantId = tenantA, AssessmentSubjectId = subjectB, ActorUserId = actorId,
                ClientRequestId = Guid.NewGuid(), Decision = AssessmentMarkReviewDecision.Approved,
                MarkRowVersion = first.MarkRowVersion,
                OccurredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        // History is immutable through EF, including soft delete.
        await using (var db = TenantDb(options, tenantA))
        {
            var eventRow = await db.AssessmentMarkReviewEvents
                .SingleAsync(x => x.Id == first.Id);
            eventRow.Reason = "Tamper attempt";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = TenantDb(options, tenantA))
        {
            var eventRow = await db.AssessmentMarkReviewEvents
                .SingleAsync(x => x.Id == first.Id);
            eventRow.IsDeleted = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = TenantDb(options, tenantA))
        {
            var rows = await db.AssessmentMarkReviewEvents.AsNoTracking().ToListAsync();
            Assert.Single(rows);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, rows[0].Decision);
            Assert.Null(rows[0].Reason);
            Assert.False(rows[0].IsDeleted);
        }
        // A long-lived approval register must expose older immutable events without
        // offset-shifting or cross-tenant rows. Exercise the same SQL keyset ordering
        // used by the authorized service history endpoint.
        await using (var db = TenantDb(options, tenantA))
        {
            var now = DateTime.UtcNow;
            db.AssessmentMarkReviewEvents.AddRange(Enumerable.Range(0, 60).Select(i =>
                new AssessmentMarkReviewEvent {
                    TenantId = tenantA, AssessmentSubjectId = subjectA, ActorUserId = actorId,
                    ClientRequestId = Guid.NewGuid(),
                    Decision = i % 2 == 0 ? AssessmentMarkReviewDecision.Submitted :
                        AssessmentMarkReviewDecision.Rejected,
                    MarkRowVersion = first.MarkRowVersion,
                    OccurredAt = now.AddSeconds(i), CreatedAt = now.AddSeconds(i)
                }));
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, tenantA))
        {
            var firstPage = await db.AssessmentMarkReviewEvents.AsNoTracking()
                .Where(x => x.TenantId == tenantA && x.AssessmentSubjectId == subjectA)
                .OrderByDescending(x => x.Id).Take(25).Select(x => x.Id).ToArrayAsync();
            var cursor = firstPage[^1];
            var secondPage = await db.AssessmentMarkReviewEvents.AsNoTracking()
                .Where(x => x.TenantId == tenantA && x.AssessmentSubjectId == subjectA &&
                    x.Id < cursor)
                .OrderByDescending(x => x.Id).Take(25).Select(x => x.Id).ToArrayAsync();
            Assert.Equal(25, firstPage.Length);
            Assert.Equal(25, secondPage.Length);
            Assert.Empty(firstPage.Intersect(secondPage));
            Assert.True(firstPage[^1] > secondPage[0]);
            Assert.False(await db.AssessmentMarkReviewEvents.AsNoTracking()
                .AnyAsync(x => x.AssessmentSubjectId == subjectB));
        }
    }

    [Fact]
    public async Task Competing_review_event_writes_roll_back_the_stale_assessment_version()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantId, subjectId, actorId;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-EXR-" + suffix, Name = "Exam review race",
                Email = "examrace-" + suffix + "@example.test", State = TenantState.Active };
            var name = "exam-race-" + suffix + "@example.test";
            var actor = new ApplicationUser { UserName = name,
                NormalizedUserName = name.ToUpperInvariant(), Email = name,
                NormalizedEmail = name.ToUpperInvariant(), FullName = "Synthetic reviewer",
                IsActive = true, EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
            setup.AddRange(tenant, actor);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; actorId = actor.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
                subjectId = await SeedSubjectAsync(setup, tenantId, suffix);
        }

        await using var first = TenantDb(options, tenantId);
        await using var second = TenantDb(options, tenantId);
        var assessmentId = await first.AssessmentSubjects.AsNoTracking()
            .Where(x => x.Id == subjectId).Select(x => x.AssessmentId).SingleAsync();
        var firstAssessment = await first.Assessments.SingleAsync(x => x.Id == assessmentId);
        var staleAssessment = await second.Assessments.SingleAsync(x => x.Id == assessmentId);
        Assert.Equal(firstAssessment.RowVersion, staleAssessment.RowVersion);
        var now = DateTime.UtcNow;
        var winningKey = Guid.NewGuid();
        var losingKey = Guid.NewGuid();

        await using (var tx = await first.Database.BeginTransactionAsync())
        {
            first.AssessmentMarkReviewEvents.Add(new AssessmentMarkReviewEvent {
                TenantId = tenantId, AssessmentSubjectId = subjectId, ActorUserId = actorId,
                ClientRequestId = winningKey, Decision = AssessmentMarkReviewDecision.Submitted,
                MarkRowVersion = Convert.ToBase64String(firstAssessment.RowVersion),
                OccurredAt = now, CreatedAt = now
            });
            firstAssessment.UpdatedAt = now.AddSeconds(1);
            await first.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await using (var tx = await second.Database.BeginTransactionAsync())
        {
            second.AssessmentMarkReviewEvents.Add(new AssessmentMarkReviewEvent {
                TenantId = tenantId, AssessmentSubjectId = subjectId, ActorUserId = actorId,
                ClientRequestId = losingKey, Decision = AssessmentMarkReviewDecision.Approved,
                MarkRowVersion = Convert.ToBase64String(staleAssessment.RowVersion),
                OccurredAt = now, CreatedAt = now
            });
            staleAssessment.UpdatedAt = now.AddSeconds(2);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => second.SaveChangesAsync());
            await tx.RollbackAsync();
        }
        await using var verify = TenantDb(options, tenantId);
        var events = await verify.AssessmentMarkReviewEvents.AsNoTracking()
            .Where(x => x.AssessmentSubjectId == subjectId).ToListAsync();
        Assert.Single(events);
        Assert.Equal(winningKey, events[0].ClientRequestId);
        Assert.False(await verify.AssessmentMarkReviewEvents.AsNoTracking()
            .AnyAsync(x => x.ClientRequestId == losingKey));
    }

    [Fact]
    public async Task Finalized_publications_are_tenant_scoped_and_withdrawn_evidence_is_immutable()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        long tenantA, tenantB, subjectId;
        await using (var setup = new EduOSDbContext(options))
        {
            var a = new Tenant { Code = "CI-FRA-" + suffix, Name = "Finalized result A",
                Email = "finala-" + suffix + "@example.test", State = TenantState.Active };
            var b = new Tenant { Code = "CI-FRB-" + suffix, Name = "Finalized result B",
                Email = "finalb-" + suffix + "@example.test", State = TenantState.Active };
            setup.AddRange(a, b);
            await setup.SaveChangesAsync();
            tenantA = a.Id; tenantB = b.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantA))
                subjectId = await SeedSubjectAsync(setup, tenantA, suffix);
        }
        long assessmentId, batchId;
        await using (var db = TenantDb(options, tenantA))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == subjectId);
            assessmentId = subject.AssessmentId;
            batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            db.ResultPublications.AddRange(
                new ResultPublication {
                    TenantId = tenantA, AssessmentId = assessmentId, AcademicBatchId = batchId,
                    VersionNo = 1, State = ResultPublicationState.Published
                },
                new ResultPublication {
                    TenantId = tenantA, AssessmentId = assessmentId, AcademicBatchId = batchId,
                    VersionNo = 2, State = ResultPublicationState.Withdrawn,
                    VisibleToStudent = false, VisibleToGuardian = false
                });
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, tenantA))
        {
            var finalized = await db.ResultPublications.AsNoTracking().Where(x =>
                x.TenantId == tenantA && x.AssessmentId == assessmentId &&
                x.AcademicBatchId == batchId && !x.IsDeleted &&
                (x.State == ResultPublicationState.Published ||
                 x.State == ResultPublicationState.Withdrawn)).CountAsync();
            Assert.Equal(2, finalized);
            var withdrawn = await db.ResultPublications.SingleAsync(x =>
                x.AssessmentId == assessmentId && x.VersionNo == 2);
            withdrawn.PublishNote = "Unapproved historical rewrite";
            db.Entry(withdrawn).Property(x => x.PublishNote).IsModified = true;
            var historicalError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.SaveChangesAsync());
            Assert.Contains("immutable", historicalError.Message.ToLowerInvariant());
            db.ChangeTracker.Clear();

            // A forged direct-ID update must not bypass snapshot immutability.
            var snapshot = new StudentResultSummary {
                Id = long.MaxValue, TenantId = tenantA, ResultPublicationId = withdrawn.Id,
                AssessmentId = assessmentId, StudentEnrollmentId = long.MaxValue,
                PublicationVersionNo = 2, ObtainedMarks = 75m, TotalMarks = 100m,
                RowVersion = new byte[8]
            };
            db.Attach(snapshot);
            snapshot.ObtainedMarks = 90m;
            db.Entry(snapshot).Property(x => x.ObtainedMarks).IsModified = true;
            var snapshotError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => db.SaveChangesAsync());
            Assert.Contains("immutable", snapshotError.Message.ToLowerInvariant());
        }
        await using (var other = TenantDb(options, tenantB))
        {
            Assert.False(await other.ResultPublications.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantB && x.AssessmentId == assessmentId &&
                x.AcademicBatchId == batchId &&
                (x.State == ResultPublicationState.Published ||
                 x.State == ResultPublicationState.Withdrawn)));
        }
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private static async Task<long> SeedSubjectAsync(EduOSDbContext db, long tenant, string suffix)
    {
        var campus = new Campus { TenantId = tenant, Name = "Main", Code = "MAIN" };
        var year = new AcademicYear { TenantId = tenant, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        var program = new AcademicProgram { TenantId = tenant, Name = "Synthetic", Code = "SYN" };
        var subject = new Subject { TenantId = tenant, Name = "Synthetic subject", Code = "SUB" };
        db.AddRange(campus, year, program, subject);
        await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = tenant, AcademicProgramId = program.Id,
            Name = "One", Code = "ONE", LevelNo = 1 };
        var curriculum = new AcademicCurriculum { TenantId = tenant, AcademicProgramId = program.Id,
            Name = "Synthetic curriculum", Code = "CUR",
            EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
        db.AddRange(level, curriculum);
        await db.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = tenant, CampusId = campus.Id,
            AcademicYearId = year.Id, AcademicProgramId = program.Id,
            AcademicLevelId = level.Id, Name = "Synthetic batch", Code = "BATCH" };
        var curriculumSubject = new CurriculumSubject { TenantId = tenant,
            AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
            SubjectId = subject.Id };
        db.AddRange(batch, curriculumSubject);
        await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = tenant,
            AcademicBatchId = batch.Id, AcademicYearId = year.Id,
            CurriculumSubjectId = curriculumSubject.Id, Code = "OFFER" };
        var exam = new Assessment { TenantId = tenant, CampusId = campus.Id,
            AcademicYearId = year.Id, Name = "Synthetic exam", Code = "EXAM",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 2),
            State = AssessmentState.MarksEntry };
        db.AddRange(offering, exam);
        await db.SaveChangesAsync();
        var examSubject = new AssessmentSubject { TenantId = tenant,
            AssessmentId = exam.Id, SubjectOfferingId = offering.Id };
        db.AssessmentSubjects.Add(examSubject);
        await db.SaveChangesAsync();
        return examSubject.Id;
    }
}
