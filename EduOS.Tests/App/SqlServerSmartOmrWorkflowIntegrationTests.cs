using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.AssessmentAdministration;
using EduOS.Service.Services.Omr;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkiaSharp;
using System.Security.Cryptography;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Disposable real-SQL full OMR service workflow, including canonical mark
/// reconciliation. The image/scanner is a controlled synthetic fixture, not a physical camera.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerSmartOmrWorkflowIntegrationTests
{
    [Fact]
    public async Task Independent_tenant_contexts_retain_their_own_request_identity()
    {
        // HttpContextAccessor has a process-wide AsyncLocal holder. Constructing a new
        // instance and assigning HttpContext for each parallel test context clears
        // the other's holder, unlike a real request's singleton accessor.
        var options = new DbContextOptionsBuilder<EduOSDbContext>().Options;
        await using var first = TenantDb(options, 17);
        await using var second = TenantDb(options, 23);
        Assert.Equal(17, first.CurrentTenantId);
        Assert.Equal(23, second.CurrentTenantId);
    }

    [Fact]
    public async Task Capture_review_post_is_atomic_retry_safe_and_tenant_scoped()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        using var png = new SKBitmap(OmrImageRecognition.SheetWidth,
            OmrImageRecognition.SheetHeight(2));
        png.Erase(SKColors.White);
        foreach (var (x, y) in new[] { (50, 50), (950, 50),
            (50, png.Height - 50), (950, png.Height - 50),
            (320, OmrImageRecognition.FirstQuestionY) })
            for (var dy = -9; dy <= 9; dy++)
            for (var dx = -9; dx <= 9; dx++)
                if (dx * dx + dy * dy <= 81) png.SetPixel(x + dx, y + dy, SKColors.Black);
        using var snapshot = SKImage.FromBitmap(png);
        using var bytes = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        var content = bytes.ToArray();
        var key = $"tenant-{seeded.TenantId}/omr/{Guid.NewGuid():N}.png";
        Guid fileReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var asset = new FileAsset
            {
                TenantId = seeded.TenantId, StorageProvider = "LocalPrivate", StorageKey = key,
                ContentType = "image/png", OriginalFileName = "synthetic-omr.png",
                SizeBytes = content.Length, Sha256 = Convert.ToHexString(SHA256.HashData(content)),
                UploadedByUserId = seeded.TeacherId, IsVerifiedSafe = true,
                MalwareScanStateCode = "Clean", MalwareScanCompletedAt = DateTime.UtcNow,
                Visibility = FileVisibility.Private, CreatedBy = seeded.TeacherId
            };
            db.FileAssets.Add(asset);
            await db.SaveChangesAsync();
            fileReference = asset.PublicId;
        }
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(key)).ReturnsAsync(new FileDownloadResult
        {
            Content = content, ContentType = "image/png", FileName = "synthetic-omr.png"
        });
        var uploadId = Guid.NewGuid();
        Guid templateRef;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var manager = Service(db, seeded, seeded.ReviewerId, "TenantAdmin", storage.Object);
            var created = await manager.CreateTemplateAsync(new CreateOmrTemplateRequestDto
            {
                AssessmentComponentId = seeded.ComponentId, Name = "SQL OMR",
                AnswerKey = "AB", OptionsPerQuestion = 4, CorrectAnswerMarks = 1m
            });
            Assert.True(created.Success, created.Message);
            templateRef = created.Data!.Reference;
        }
        Guid sheetRef;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var teacher = Service(db, seeded, seeded.TeacherId, "Teacher", storage.Object);
            var foreignImage = await teacher.CaptureAsync(new CaptureOmrSheetRequestDto
            {
                RequestId = Guid.NewGuid(), TemplateReference = templateRef,
                StudentSubjectRegistrationId = seeded.RegistrationId,
                FileReference = Guid.NewGuid()
            });
            Assert.False(foreignImage.Success);
            Assert.Equal(404, foreignImage.StatusCode);
            var capture = new CaptureOmrSheetRequestDto
            {
                RequestId = uploadId, TemplateReference = templateRef,
                StudentSubjectRegistrationId = seeded.RegistrationId, FileReference = fileReference
            };
            var first = await teacher.CaptureAsync(capture);
            Assert.True(first.Success, first.Message);
            Assert.Equal("Captured", first.Data!.State);
            sheetRef = first.Data.Reference;
            var replay = await teacher.CaptureAsync(capture);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(sheetRef, replay.Data!.Reference);
            var selfReview = await teacher.ReviewAsync(sheetRef, new ReviewOmrSheetRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = first.Data.RowVersion,
                ConfirmedAnswers = "A-", IdentityConfirmed = true
            });
            Assert.False(selfReview.Success);
            Assert.Equal(403, selfReview.StatusCode);
        }
        // A closed assessment must reject review without changing the sheet or
        // appending review history. The disposable fixture then reopens marks.
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var examId = await db.AssessmentSubjects.AsNoTracking().Where(x =>
                x.Id == seeded.SubjectId).Select(x => x.AssessmentId).SingleAsync();
            var exam = await db.Assessments.SingleAsync(x => x.Id == examId);
            exam.State = AssessmentState.Locked;
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var manager = Service(db, seeded, seeded.ReviewerId, "TenantAdmin", storage.Object);
            var sheet = await manager.GetAsync(sheetRef);
            Assert.True(sheet.Success);
            var blocked = await manager.ReviewAsync(sheetRef, new ReviewOmrSheetRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = sheet.Data!.RowVersion,
                ConfirmedAnswers = "A-", IdentityConfirmed = true
            });
            Assert.False(blocked.Success);
            Assert.Equal(409, blocked.StatusCode);
            Assert.False(await db.OmrSheetEvents.AnyAsync());
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var examId = await db.AssessmentSubjects.AsNoTracking().Where(x =>
                x.Id == seeded.SubjectId).Select(x => x.AssessmentId).SingleAsync();
            var exam = await db.Assessments.SingleAsync(x => x.Id == examId);
            exam.State = AssessmentState.MarksEntry;
            await db.SaveChangesAsync();
        }
        Guid reviewId;
        Guid postId = Guid.NewGuid();
        string reviewedVersion;
        // Two independent connections race with the same rowversion. Exactly
        // one review commits; the loser cannot overwrite approved evidence.
        await using (var firstDb = TenantDb(options, seeded.TenantId))
        await using (var secondDb = TenantDb(options, seeded.TenantId))
        {
            var firstManager = Service(firstDb, seeded, seeded.ReviewerId, "TenantAdmin", storage.Object);
            var secondManager = Service(secondDb, seeded, seeded.ReviewerId, "TenantAdmin", storage.Object);
            var sheet = await firstManager.GetAsync(sheetRef);
            Assert.True(sheet.Success);
            var firstRequest = new ReviewOmrSheetRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = sheet.Data!.RowVersion,
                ConfirmedAnswers = "A-", IdentityConfirmed = true
            };
            var secondRequest = new ReviewOmrSheetRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = sheet.Data.RowVersion,
                ConfirmedAnswers = "A-", IdentityConfirmed = true
            };
            var results = await Task.WhenAll(firstManager.ReviewAsync(sheetRef, firstRequest),
                secondManager.ReviewAsync(sheetRef, secondRequest));
            Assert.Equal(1, results.Count(x => x.Success));
            Assert.Equal(1, results.Count(x => !x.Success && x.StatusCode == 409));
            var winner = results.Single(x => x.Success);
            Assert.Equal("Reviewed", winner.Data!.State);
            reviewedVersion = winner.Data.RowVersion;
            reviewId = results[0].Success ? firstRequest.RequestId : secondRequest.RequestId;
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var manager = Service(db, seeded, seeded.ReviewerId, "TenantAdmin", storage.Object);
            var repeated = await manager.ReviewAsync(sheetRef, new ReviewOmrSheetRequestDto
            {
                RequestId = reviewId, RowVersion = "AQAAAAAAAAA=",
                ConfirmedAnswers = "A-", IdentityConfirmed = true
            });
            Assert.True(repeated.Success, repeated.Message);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var examId = await db.AssessmentSubjects.AsNoTracking().Where(x =>
                x.Id == seeded.SubjectId).Select(x => x.AssessmentId).SingleAsync();
            var exam = await db.Assessments.SingleAsync(x => x.Id == examId);
            exam.State = AssessmentState.Locked;
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var manager = Service(db, seeded, seeded.ReviewerId, "TenantAdmin", storage.Object);
            var blocked = await manager.PostReviewedAsync(sheetRef, new PostOmrSheetRequestDto
            {
                RequestId = postId, RowVersion = reviewedVersion
            });
            Assert.False(blocked.Success);
            Assert.Equal(409, blocked.StatusCode);
            Assert.False(await db.StudentAssessmentMarks.AnyAsync(x =>
                x.AssessmentSubjectId == seeded.SubjectId &&
                x.StudentSubjectRegistrationId == seeded.RegistrationId));
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var examId = await db.AssessmentSubjects.AsNoTracking().Where(x =>
                x.Id == seeded.SubjectId).Select(x => x.AssessmentId).SingleAsync();
            var exam = await db.Assessments.SingleAsync(x => x.Id == examId);
            exam.State = AssessmentState.MarksEntry;
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var manager = Service(db, seeded, seeded.ReviewerId, "TenantAdmin", storage.Object);
            var posted = await manager.PostReviewedAsync(sheetRef, new PostOmrSheetRequestDto
            {
                RequestId = postId, RowVersion = reviewedVersion
            });
            Assert.True(posted.Success, posted.Message);
            Assert.Equal("Posted", posted.Data!.State);
            Assert.True(posted.Data.ProposedMarks.HasValue);
            Assert.Equal(1m, posted.Data.ProposedMarks.Value);
            var retry = await manager.PostReviewedAsync(sheetRef, new PostOmrSheetRequestDto
            {
                RequestId = postId, RowVersion = reviewedVersion
            });
            Assert.True(retry.Success, retry.Message);
            var otherKey = await manager.PostReviewedAsync(sheetRef, new PostOmrSheetRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = reviewedVersion
            });
            Assert.False(otherKey.Success);
            Assert.Equal(409, otherKey.StatusCode);
            var marks = await db.StudentAssessmentMarks.AsNoTracking().Where(x =>
                x.AssessmentSubjectId == seeded.SubjectId &&
                x.StudentSubjectRegistrationId == seeded.RegistrationId).ToArrayAsync();
            var mark = Assert.Single(marks);
            Assert.Equal(1m, mark.ObtainedMarks);
            var details = await db.StudentAssessmentComponentMarks.AsNoTracking().Where(x =>
                x.StudentAssessmentMarkId == mark.Id &&
                x.AssessmentComponentId == seeded.ComponentId).ToArrayAsync();
            Assert.Equal(1m, Assert.Single(details).ObtainedMarks);
            var events = await db.OmrSheetEvents.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(new[] { "Reviewed", "Posted" }, events.Select(x => x.Action));
            Assert.Equal(2, events.Select(x => x.ClientRequestId).Distinct().Count());
        }
        // A valid user of another institute cannot read or post a guessed sheet ID.
        await using (var wrongTenant = TenantDb(options, seeded.OtherTenantId))
        {
            var outsider = Service(wrongTenant, seeded, seeded.ReviewerId,
                "TenantAdmin", storage.Object, seeded.OtherTenantId);
            var get = await outsider.GetAsync(sheetRef);
            Assert.False(get.Success);
            Assert.Equal(404, get.StatusCode);
            var post = await outsider.PostReviewedAsync(sheetRef, new PostOmrSheetRequestDto
            {
                RequestId = Guid.NewGuid(), RowVersion = reviewedVersion
            });
            Assert.False(post.Success);
            Assert.Equal(404, post.StatusCode);
        }
        // Tenant composite FKs deny direct inserts referencing another institution's sheet lineage.
        OmrSheet existing;
        await using (var owner = TenantDb(options, seeded.TenantId))
            existing = await owner.OmrSheets.AsNoTracking().SingleAsync(x => x.PublicId == sheetRef);
        await using (var db = TenantDb(options, seeded.OtherTenantId))
        {
            db.OmrSheets.Add(new OmrSheet
            {
                TenantId = seeded.OtherTenantId, ClientRequestId = Guid.NewGuid(),
                OmrTemplateId = existing.OmrTemplateId,
                StudentSubjectRegistrationId = seeded.RegistrationId,
                FileAssetId = existing.FileAssetId,
                SuggestedAnswers = "A?", SuggestionStates = "SB",
                CapturedByUserId = seeded.TeacherId
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        // SQL versions and the template/event history are protected independently of the UI.
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var history = await db.OmrSheetEvents.FirstAsync(x => x.Action == "Reviewed");
            history.Reason = "history tamper";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var template = await db.OmrTemplates.SingleAsync(x => x.PublicId == templateRef);
            template.AnswerKey = "BB";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    private static OmrWorkflowService Service(EduOSDbContext db, Seeded seeded, long actorId,
        string role, IFileUploadService storage, long? tenant = null)
    {
        var user = new Actor(tenant ?? seeded.TenantId, actorId, role);
        return new OmrWorkflowService(db, user, storage, Marks(db, user),
            new ConfigurationBuilder().Build(), NullLogger<OmrWorkflowService>.Instance);
    }

    private static AssessmentAdministrationService Marks(EduOSDbContext db, Actor actor)
    {
        GenericRepository<T> Repo<T>() where T : class => new(db);
        return new AssessmentAdministrationService(
            Repo<Assessment>(), Repo<AssessmentSubject>(), Repo<AssessmentMarkReviewEvent>(),
            Repo<AssessmentSchedule>(), Repo<StudentAssessmentMark>(), Repo<ResultPublication>(),
            Repo<StudentResultSummary>(), Repo<GradeScheme>(), Repo<GradeRule>(),
            Repo<StudentEnrollment>(), Repo<Subject>(), Repo<StudentSubjectRegistration>(),
            Repo<SubjectOffering>(), Repo<CurriculumSubject>(), Repo<AcademicBatch>(),
            Repo<AcademicYear>(), Repo<AcademicTerm>(), Repo<Campus>(), Repo<UserCampusAccess>(),
            Repo<Room>(), Repo<Student>(), Repo<Employee>(), Repo<InstructorAssignment>(),
            Repo<AssessmentComponent>(), Repo<StudentAssessmentComponentMark>(),
            Repo<CertificateTemplate>(), Repo<CertificateIssue>(), Repo<TranscriptIssue>(),
            Repo<EduOS.Core.Entities.System.AuditLog>(), db, actor, TimeProvider.System,
            NullLogger<AssessmentAdministrationService>.Instance,
            Repo<ResultRescrutinyEvent>(), Repo<Guardian>(), Repo<StudentGuardian>(),
            Repo<ResultCorrectionEvent>(), Repo<StudentResultCourseSnapshot>(),
            null, null, Repo<AssessmentAttemptRegistration>(), obeDb: db);
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new IsolatedHttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private sealed class IsolatedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static async Task<Seeded> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = new EduOSDbContext(options);
        var firstTenant = new Tenant { Code = "CI-OMR-A-" + suffix, Name = "Synthetic OMR A",
            Email = "omra-" + suffix + "@example.test", State = TenantState.Active };
        var secondTenant = new Tenant { Code = "CI-OMR-B-" + suffix, Name = "Synthetic OMR B",
            Email = "omrb-" + suffix + "@example.test", State = TenantState.Active };
        ApplicationUser User(string tag)
        {
            var email = $"omr-{tag}-{suffix}@example.test";
            return new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), FullName = tag,
                IsActive = true, EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
        }
        var capturer = User("capture"); var reviewer = User("review");
        var teacherPerson = new Person { FullName = "Synthetic OMR teacher" };
        var studentPerson = new Person { FullName = "Synthetic OMR pupil" };
        db.AddRange(firstTenant, secondTenant, capturer, reviewer, teacherPerson, studentPerson);
        await db.SaveChangesAsync();
        var tenant = firstTenant.Id;
        db.ChangeTracker.Clear();
        using (db.BeginSystemTenantScope(tenant))
        {
            var campus = new Campus { TenantId = tenant, Code = "MAIN", Name = "Main" };
            var year = new AcademicYear { TenantId = tenant, Name = "2026", Code = "2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
            var program = new AcademicProgram { TenantId = tenant, Name = "OMR", Code = "OMR" };
            var subject = new Subject { TenantId = tenant, Name = "OMR Mathematics", Code = "MATH" };
            var designation = new Designation { TenantId = tenant, Name = "Teacher", Code = "T" };
            var student = new Student { TenantId = tenant, PersonId = studentPerson.Id,
                FullName = "Synthetic pupil", StudentCode = "OMR-" + suffix,
                AdmissionDate = new DateOnly(2026, 1, 1), StatusCode = "Active" };
            db.AddRange(campus, year, program, subject, designation, student);
            await db.SaveChangesAsync();
            var teacher = new Employee { TenantId = tenant, PersonId = teacherPerson.Id,
                DesignationId = designation.Id, UserId = capturer.Id, EmployeeCode = "OMR-T-" + suffix,
                FullName = "Synthetic teacher", CanTeach = true, State = EmployeeState.Active,
                JoiningDate = new DateOnly(2026, 1, 1) };
            var level = new AcademicLevel { TenantId = tenant, AcademicProgramId = program.Id,
                Name = "One", Code = "ONE", LevelNo = 1 };
            var curriculum = new AcademicCurriculum { TenantId = tenant,
                AcademicProgramId = program.Id, Name = "OMR curriculum", Code = "CUR",
                EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
            db.AddRange(teacher, level, curriculum);
            await db.SaveChangesAsync();
            var batch = new AcademicBatch { TenantId = tenant, CampusId = campus.Id,
                AcademicYearId = year.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, Name = "One", Code = "A" };
            var curriculumSubject = new CurriculumSubject { TenantId = tenant,
                AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id, SubjectId = subject.Id };
            db.AddRange(batch, curriculumSubject);
            await db.SaveChangesAsync();
            var offering = new SubjectOffering { TenantId = tenant,
                AcademicBatchId = batch.Id, AcademicYearId = year.Id,
                CurriculumSubjectId = curriculumSubject.Id, Code = "MATH" };
            var exam = new Assessment { TenantId = tenant, CampusId = campus.Id,
                AcademicYearId = year.Id, Name = "OMR final", Code = "OMR-FINAL",
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 2),
                State = AssessmentState.MarksEntry };
            var enrollment = new StudentEnrollment { TenantId = tenant, StudentId = student.Id,
                AcademicYearId = year.Id, CampusId = campus.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                AcademicCurriculumId = curriculum.Id, RollNo = "01",
                EnrollmentDate = new DateOnly(2026, 1, 1), State = EnrollmentState.Active,
                IsCurrent = true, ClientRequestId = Guid.NewGuid() };
            db.AddRange(offering, exam, enrollment);
            await db.SaveChangesAsync();
            var assessmentSubject = new AssessmentSubject { TenantId = tenant,
                AssessmentId = exam.Id, SubjectOfferingId = offering.Id, FullMarks = 100m };
            var registration = new StudentSubjectRegistration { TenantId = tenant,
                StudentEnrollmentId = enrollment.Id, SubjectOfferingId = offering.Id,
                ClientRequestId = Guid.NewGuid(), State = SubjectRegistrationState.Approved };
            var assignment = new InstructorAssignment { TenantId = tenant,
                SubjectOfferingId = offering.Id, EmployeeId = teacher.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
            db.AddRange(assessmentSubject, registration, assignment);
            await db.SaveChangesAsync();
            var component = new AssessmentComponent { TenantId = tenant,
                AssessmentSubjectId = assessmentSubject.Id, Name = "OMR component",
                Code = "OMR", FullMarks = 20m, PassMarks = 1m };
            db.AssessmentComponents.Add(component);
            await db.SaveChangesAsync();
            return new Seeded(tenant, secondTenant.Id, capturer.Id, reviewer.Id,
                assessmentSubject.Id, registration.Id, component.Id);
        }
    }

    private sealed record Seeded(long TenantId, long OtherTenantId, long TeacherId,
        long ReviewerId, long SubjectId, long RegistrationId, long ComponentId);

    private sealed class Actor(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Synthetic OMR actor";
        public string? Email => "omr@example.test";
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL OMR integration";
    }
}
