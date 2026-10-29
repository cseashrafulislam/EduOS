using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.AssessmentAdministration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Exercises the real assessment service against disposable SQL Server, including
/// instructor ownership, reviewer separation, rejected-mark correction, replay,
/// optimistic versions and frozen approved evidence. No production records.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerAssessmentMarkReviewLifecycleIntegrationTests
{
    [Theory]
    [InlineData(0, false, 7, false)] // Reject: distinct courses -> (4 + 3) / 2
    [InlineData(0, true, -1, false)] // Reject: repeated course must fail closed
    [InlineData(1, true, 6, false)]  // Latest attempt replaces earlier grade of the same course
    [InlineData(2, true, 8, false)]  // Highest grade preserves the earlier, better course attempt
    [InlineData(2, true, 6, true)]   // Withdrawn v1 must not supersede the corrected v2 grade
    public async Task Cumulative_credit_gpa_uses_only_published_historical_course_snapshots(
        int repeatPolicy, bool repeatedCourse, int twiceExpectedCgpa, bool supersededHistory)
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        var expectedCgpa = twiceExpectedCgpa / 2m;
        long assessmentId;
        long batchId;
        Guid assessmentReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var currentExam = await db.Assessments.SingleAsync(x => x.Id == subject.AssessmentId);
            var offering = await db.SubjectOfferings.SingleAsync(x => x.Id == subject.SubjectOfferingId);
            var originalCourse = await db.CurriculumSubjects.SingleAsync(x => x.Id == offering.CurriculumSubjectId);
            var registration = await db.StudentSubjectRegistrations.SingleAsync(x => x.Id == seeded.RegistrationId);
            var enrollment = await db.StudentEnrollments.SingleAsync(x => x.Id == registration.StudentEnrollmentId);
            originalCourse.CreditHours = 2m;
            registration.CreditHoursSnapshot = 2m;
            var scheme = new GradeScheme { TenantId = seeded.TenantId,
                Name = "Transcript credits", Code = "CGPA-" + Guid.NewGuid().ToString("N")[..12],
                UseCreditWeightedGpa = true,
                RepeatCourseGradePolicy = (RepeatCourseGradePolicy)repeatPolicy };
            db.GradeSchemes.Add(scheme);
            await db.SaveChangesAsync();
            db.GradeRules.AddRange(
                new GradeRule { TenantId = seeded.TenantId, GradeSchemeId = scheme.Id,
                    GradeLetter = "B", GradePoint = 3m, MinMarks = 0m, MaxMarks = 79.99m },
                new GradeRule { TenantId = seeded.TenantId, GradeSchemeId = scheme.Id,
                    GradeLetter = "A", GradePoint = 4m, MinMarks = 80m, MaxMarks = 100m });
            currentExam.State = AssessmentState.Locked;
            currentExam.GradeSchemeId = scheme.Id;
            currentExam.CountsTowardCgpa = true;
            var historic = new Assessment { TenantId = seeded.TenantId,
                CampusId = currentExam.CampusId, AcademicYearId = currentExam.AcademicYearId,
                Name = "Prior term exam", Code = "PRIOR-" + Guid.NewGuid().ToString("N")[..8],
                StartDate = new DateOnly(2026, 5, 1), EndDate = new DateOnly(2026, 5, 2),
                State = AssessmentState.Locked, GradeSchemeId = scheme.Id,
                CountsTowardCgpa = true };
            db.Assessments.Add(historic);
            var otherSubject = new Subject { TenantId = seeded.TenantId,
                Name = "History", Code = "HIST-" + Guid.NewGuid().ToString("N")[..8] };
            db.Subjects.Add(otherSubject);
            await db.SaveChangesAsync();
            var pastCourse = new CurriculumSubject { TenantId = seeded.TenantId,
                AcademicCurriculumId = originalCourse.AcademicCurriculumId,
                AcademicLevelId = originalCourse.AcademicLevelId,
                SubjectId = otherSubject.Id, CreditHours = 2m };
            db.CurriculumSubjects.Add(pastCourse);
            await db.SaveChangesAsync();
            var pastOffering = new SubjectOffering { TenantId = seeded.TenantId,
                AcademicBatchId = offering.AcademicBatchId, AcademicYearId = offering.AcademicYearId,
                CurriculumSubjectId = repeatedCourse ? originalCourse.Id : pastCourse.Id,
                Code = "HIST" };
            db.SubjectOfferings.Add(pastOffering);
            await db.SaveChangesAsync();
            var pastSubject = new AssessmentSubject { TenantId = seeded.TenantId,
                AssessmentId = historic.Id, SubjectOfferingId = pastOffering.Id };
            var pastRegistration = new StudentSubjectRegistration { TenantId = seeded.TenantId,
                StudentEnrollmentId = enrollment.Id, SubjectOfferingId = pastOffering.Id,
                ClientRequestId = Guid.NewGuid(), CreditHoursSnapshot = 2m,
                State = SubjectRegistrationState.Approved };
            db.AssessmentSubjects.Add(pastSubject);
            db.StudentSubjectRegistrations.Add(pastRegistration);
            await db.SaveChangesAsync();
            var pastPublication = new ResultPublication { TenantId = seeded.TenantId,
                AssessmentId = historic.Id, AcademicBatchId = offering.AcademicBatchId,
                VersionNo = 1, State = ResultPublicationState.Published,
                HasCourseGradeSnapshots = true, PublishedByUserId = seeded.ReviewerId,
                PublishedAt = DateTime.UtcNow.AddDays(-20) };
            db.ResultPublications.Add(pastPublication);
            await db.SaveChangesAsync();
            db.StudentResultSummaries.Add(new StudentResultSummary { TenantId = seeded.TenantId,
                ResultPublicationId = pastPublication.Id, StudentEnrollmentId = enrollment.Id,
                AssessmentId = historic.Id, PublicationVersionNo = 1,
                TotalMarks = 100m, ObtainedMarks = 90m, Percentage = 90m,
                GPA = 4m, CGPA = 4m, CreditHours = 2m, CourseCount = 1,
                GradeLetter = "A", IsPassed = true });
            db.StudentResultCourseSnapshots.Add(new StudentResultCourseSnapshot {
                TenantId = seeded.TenantId, ResultPublicationId = pastPublication.Id,
                StudentEnrollmentId = enrollment.Id, AssessmentSubjectId = pastSubject.Id,
                CurriculumSubjectId = pastOffering.CurriculumSubjectId,
                StudentSubjectRegistrationId = pastRegistration.Id,
                FullMarks = 100m, ObtainedMarks = 90m, CreditHours = 2m, GradePoint = 4m,
                GradeLetter = "A", IsPassed = true });
            await db.SaveChangesAsync();
            if (supersededHistory)
            {
                // Arrange a permitted one-way withdrawal before a newer
                // version; the old immutable evidence must not enter CGPA.
                pastPublication.State = ResultPublicationState.Withdrawn;
                pastPublication.VisibleToStudent = false;
                pastPublication.VisibleToGuardian = false;
                pastPublication.UpdatedAt = DateTime.UtcNow;
                pastPublication.UpdatedBy = seeded.ReviewerId;
                await db.SaveChangesAsync();
                var corrected = new ResultPublication { TenantId = seeded.TenantId,
                    AssessmentId = historic.Id, AcademicBatchId = offering.AcademicBatchId,
                    VersionNo = 2, State = ResultPublicationState.Published,
                    HasCourseGradeSnapshots = true,
                    PublishedByUserId = seeded.ReviewerId, PublishedAt = DateTime.UtcNow.AddDays(-19) };
                db.ResultPublications.Add(corrected);
                await db.SaveChangesAsync();
                db.StudentResultSummaries.Add(new StudentResultSummary { TenantId = seeded.TenantId,
                    ResultPublicationId = corrected.Id, StudentEnrollmentId = enrollment.Id,
                    AssessmentId = historic.Id, PublicationVersionNo = 2,
                    TotalMarks = 100m, ObtainedMarks = 70m, Percentage = 70m,
                    GPA = 3m, CGPA = 3m, CreditHours = 2m, CourseCount = 1,
                    GradeLetter = "B", IsPassed = true });
                db.StudentResultCourseSnapshots.Add(new StudentResultCourseSnapshot {
                    TenantId = seeded.TenantId, ResultPublicationId = corrected.Id,
                    StudentEnrollmentId = enrollment.Id, AssessmentSubjectId = pastSubject.Id,
                    CurriculumSubjectId = pastOffering.CurriculumSubjectId,
                    StudentSubjectRegistrationId = pastRegistration.Id,
                    FullMarks = 100m, ObtainedMarks = 70m, CreditHours = 2m,
                    GradePoint = 3m, GradeLetter = "B", IsPassed = true });
                await db.SaveChangesAsync();
            }
            assessmentId = currentExam.Id;
            assessmentReference = currentExam.PublicId;
            batchId = offering.AcademicBatchId;
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var draft = await db.Assessments.SingleAsync(x => x.Id == assessmentId);
            draft.CountsTowardCgpa = false;
            await db.SaveChangesAsync();
            var excluded = await service.PreviewResultsAsync(new() {
                AssessmentId = assessmentId, AcademicBatchId = batchId });
            Assert.True(excluded.Success, excluded.Message);
            Assert.Null(Assert.Single(excluded.Data!.Results).CumulativeGradePointAverage);
            draft.CountsTowardCgpa = true;
            await db.SaveChangesAsync();
            var preview = await service.PreviewResultsAsync(new() {
                AssessmentId = assessmentId, AcademicBatchId = batchId });
            if (twiceExpectedCgpa < 0)
            {
                Assert.False(preview.Success);
                Assert.Equal(409, preview.StatusCode);
                return;
            }
            Assert.True(preview.Success, preview.Message);
            var row = Assert.Single(preview.Data!.Results);
            Assert.Equal(3m, row.GradePointAverage);
            Assert.Equal(expectedCgpa, row.CumulativeGradePointAverage);

            var published = await service.PublishResultAsync(new() {
                ClientRequestId = Guid.NewGuid(), AssessmentReference = assessmentReference,
                AcademicBatchId = batchId, VisibleToStudent = true, VisibleToGuardian = true });
            Assert.True(published.Success, published.Message);
            var saved = await db.StudentResultSummaries.AsNoTracking()
                .SingleAsync(x => x.ResultPublicationId == published.Data!.Id);
            Assert.Equal(expectedCgpa, saved.CGPA);
            Assert.Equal(2m, saved.CreditHours);
            var courseGrade = await db.StudentResultCourseSnapshots.AsNoTracking()
                .SingleAsync(x => x.ResultPublicationId == published.Data!.Id);
            Assert.Equal(2m, courseGrade.CreditHours);
            Assert.Equal(3m, courseGrade.GradePoint);
            Assert.Equal(1, saved.CourseCount);
            var readable = await service.GetResultsAsync(new() {
                AssessmentId = assessmentId, AcademicBatchId = batchId });
            Assert.True(readable.Success, readable.Message);
            Assert.Equal(expectedCgpa, Assert.Single(readable.Data!.Results).CumulativeGradePointAverage);
            var currentEnrollment = await db.StudentEnrollments.AsNoTracking()
                .SingleAsync(x => x.AcademicBatchId == batchId && x.IsCurrent);
            var pupil = await db.Students.AsNoTracking()
                .SingleAsync(x => x.Id == currentEnrollment.StudentId);
            var history = await service.GetTranscriptAcademicRecordAsync(pupil.PublicId);
            Assert.True(history.Success, history.Message);
            Assert.Equal(2, history.Data!.Courses.Count);
            Assert.Equal(expectedCgpa, history.Data!.LatestPublishedCgpa);
            var isolated = Service(db,
                new Actor(seeded.TenantId + 100_000, seeded.ReviewerId, "TenantAdmin"));
            var forbidden = await isolated.GetTranscriptAcademicRecordAsync(pupil.PublicId);
            Assert.False(forbidden.Success);
            // Even a soft-deleted immutable course must invalidate the entire published
            // version. Nothing silently falls back to editable marks.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE StudentResultCourseSnapshots SET IsDeleted = 1 WHERE Id = {courseGrade.Id}");
            var broken = await service.GetResultsAsync(new() {
                AssessmentId = assessmentId, AcademicBatchId = batchId });
            Assert.False(broken.Success);
            Assert.Equal(409, broken.StatusCode);
            var damagedHistory = await service.GetTranscriptAcademicRecordAsync(pupil.PublicId);
            Assert.False(damagedHistory.Success);
            Assert.Equal(409, damagedHistory.StatusCode);
        }
    }

    [Fact]
    public async Task Credit_weighted_gpa_uses_registered_course_credits_and_opt_in_grade_scheme()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long examId;
        long batchId;
        long schemeId;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var initial = await db.AssessmentSubjects.SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var offering = await db.SubjectOfferings.SingleAsync(x => x.Id == initial.SubjectOfferingId);
            var originalCourse = await db.CurriculumSubjects.SingleAsync(x => x.Id == offering.CurriculumSubjectId);
            var registration = await db.StudentSubjectRegistrations.SingleAsync(x => x.Id == seeded.RegistrationId);
            var exam = await db.Assessments.SingleAsync(x => x.Id == initial.AssessmentId);
            originalCourse.CreditHours = 1m;
            registration.CreditHoursSnapshot = 1m;
            var scheme = new GradeScheme
            {
                TenantId = seeded.TenantId, Name = "Credit weighted", Code = "GPA-" + Guid.NewGuid().ToString("N")[..12],
                UseCreditWeightedGpa = true
            };
            db.GradeSchemes.Add(scheme);
            await db.SaveChangesAsync();
            schemeId = scheme.Id;
            exam.GradeSchemeId = schemeId;
            db.GradeRules.AddRange(
                new GradeRule { TenantId = seeded.TenantId, GradeSchemeId = schemeId,
                    GradeLetter = "F", GradePoint = 0m, MinMarks = 0m, MaxMarks = 49.99m, IsFailGrade = true },
                new GradeRule { TenantId = seeded.TenantId, GradeSchemeId = schemeId,
                    GradeLetter = "B", GradePoint = 3m, MinMarks = 50m, MaxMarks = 79.99m },
                new GradeRule { TenantId = seeded.TenantId, GradeSchemeId = schemeId,
                    GradeLetter = "A", GradePoint = 4m, MinMarks = 80m, MaxMarks = 100m });

            var science = new Subject { TenantId = seeded.TenantId,
                Name = "Science", Code = "SCI-" + Guid.NewGuid().ToString("N")[..8] };
            db.Subjects.Add(science);
            await db.SaveChangesAsync();
            var course = new CurriculumSubject { TenantId = seeded.TenantId,
                AcademicCurriculumId = originalCourse.AcademicCurriculumId,
                AcademicLevelId = originalCourse.AcademicLevelId,
                SubjectId = science.Id, CreditHours = 3m };
            db.CurriculumSubjects.Add(course);
            await db.SaveChangesAsync();
            var scienceOffering = new SubjectOffering { TenantId = seeded.TenantId,
                AcademicBatchId = offering.AcademicBatchId, AcademicYearId = offering.AcademicYearId,
                CurriculumSubjectId = course.Id, Code = "SCI" };
            db.SubjectOfferings.Add(scienceOffering);
            await db.SaveChangesAsync();
            var scienceSubject = new AssessmentSubject { TenantId = seeded.TenantId,
                AssessmentId = exam.Id, SubjectOfferingId = scienceOffering.Id };
            var scienceRegistration = new StudentSubjectRegistration { TenantId = seeded.TenantId,
                StudentEnrollmentId = registration.StudentEnrollmentId,
                SubjectOfferingId = scienceOffering.Id, ClientRequestId = Guid.NewGuid(),
                CreditHoursSnapshot = 3m, State = SubjectRegistrationState.Approved };
            db.AssessmentSubjects.Add(scienceSubject);
            db.StudentSubjectRegistrations.Add(scienceRegistration);
            await db.SaveChangesAsync();
            db.StudentAssessmentMarks.Add(new StudentAssessmentMark {
                TenantId = seeded.TenantId, AssessmentSubjectId = scienceSubject.Id,
                StudentSubjectRegistrationId = scienceRegistration.Id,
                ObtainedMarks = 90m, EnteredByUserId = seeded.TeacherId });
            await db.SaveChangesAsync();
            examId = exam.Id;
            batchId = offering.AcademicBatchId;
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var credit = await service.PreviewResultsAsync(new() {
                AssessmentId = examId, AcademicBatchId = batchId });
            Assert.True(credit.Success, credit.Message);
            var row = Assert.Single(credit.Data!.Results);
            Assert.Equal(3.7500m, row.GradePointAverage);
            Assert.Equal(82.5m, row.Percentage);
            Assert.Equal("A", row.GradeLetter);
            var scheme = await db.GradeSchemes.SingleAsync(x => x.Id == schemeId);
            // Test-only direct toggle proves pre-existing schemes retain legacy grading.
            scheme.UseCreditWeightedGpa = false;
            await db.SaveChangesAsync();
            var legacy = await service.PreviewResultsAsync(new() {
                AssessmentId = examId, AcademicBatchId = batchId });
            Assert.True(legacy.Success, legacy.Message);
            Assert.Equal(4m, Assert.Single(legacy.Data!.Results).GradePointAverage);
        }
    }

    [Fact]
    public async Task Published_result_rescrutiny_is_tenant_scoped_idempotent_and_independently_decided()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        Guid studentReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var registration = await db.StudentSubjectRegistrations.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.RegistrationId);
            var enrollment = await db.StudentEnrollments.AsNoTracking()
                .SingleAsync(x => x.Id == registration.StudentEnrollmentId);
            studentReference = await db.Students.AsNoTracking()
                .Where(x => x.Id == enrollment.StudentId).Select(x => x.PublicId).SingleAsync();
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Published, PublishedAt = DateTime.UtcNow,
                PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = subject.AssessmentId,
                StudentEnrollmentId = enrollment.Id, PublicationVersionNo = 1,
                TotalMarks = 100m, ObtainedMarks = 70m, Percentage = 70m,
                GPA = 3m, GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        var requestKey = Guid.NewGuid();
        Guid caseReference;
        string version;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var request = new RequestResultRescrutinyDto
            {
                ClientRequestId = requestKey, PublicationId = publicationId,
                StudentReference = studentReference, Reason = "Please verify the published mark."
            };
            var created = await service.RequestResultRescrutinyAsync(request);
            Assert.True(created.Success, created.Message);
            caseReference = created.Data!.CaseReference;
            version = created.Data.RequestRowVersion;
            Assert.Equal(ResultRescrutinyAction.Requested, created.Data.Status);
            var replay = await service.RequestResultRescrutinyAsync(request);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(caseReference, replay.Data!.CaseReference);
            var duplicate = await service.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
                StudentReference = studentReference, Reason = "A second duplicate application."
            });
            Assert.Equal(409, duplicate.StatusCode);
            var page = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, PageSize = 1
            });
            Assert.True(page.Success, page.Message);
            Assert.Single(page.Data!.Items);
            Assert.False(page.Data.HasMore);
            var workload = await service.GetResultRescrutinySummaryAsync(publicationId);
            Assert.True(workload.Success, workload.Message);
            Assert.Equal(1, workload.Data!.Total);
            Assert.Equal(1, workload.Data.Pending);
            Assert.Equal(0, workload.Data.AcceptedForCorrection);
            Assert.NotNull(workload.Data.OldestPendingAt);
            var export = await service.ExportResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, PageSize = 50
            });
            Assert.True(export.Success, export.Message);
            Assert.False(export.Data!.Truncated);
            Assert.Equal(caseReference, Assert.Single(export.Data.Items).CaseReference);
            var history = await service.GetResultRescrutinyHistoryAsync(caseReference);
            Assert.True(history.Success, history.Message);
            Assert.Equal(studentReference, history.Data!.StudentReference);
            Assert.Equal(ResultRescrutinyAction.Requested, Assert.Single(history.Data.Events).Action);
            var review = await service.GetResultRescrutinyReviewAsync(caseReference);
            Assert.True(review.Success, review.Message);
            Assert.Equal(studentReference, review.Data!.StudentReference);
            Assert.Equal(70m, review.Data.ObtainedMarks);
            Assert.Equal(100m, review.Data.TotalMarks);
            Assert.Equal(3m, review.Data.GPA);
            Assert.Equal(ResultRescrutinyAction.Requested, review.Data.Status);
            Assert.Equal(version, review.Data.RequestRowVersion);
            var pending = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Requested
            });
            Assert.True(pending.Success, pending.Message);
            Assert.Single(pending.Data!.Items);
            var rejected = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Rejected
            });
            Assert.True(rejected.Success, rejected.Message);
            Assert.Empty(rejected.Data!.Items);

            // A structurally valid but orphan terminal event must not vanish
            // behind the Pending filter or yield misleading workload/export totals.
            var orphan = new ResultRescrutinyEvent
            {
                TenantId = seeded.TenantId,
                CaseReference = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
                ResultPublicationId = publicationId,
                StudentEnrollmentId = await db.ResultRescrutinyEvents.AsNoTracking()
                    .Where(x => x.CaseReference == caseReference && !x.IsTerminal)
                    .Select(x => x.StudentEnrollmentId).SingleAsync(),
                PublicationVersionNo = 1, Action = ResultRescrutinyAction.Rejected,
                IsTerminal = true, ActorUserId = seeded.TeacherId,
                OccurredAt = DateTime.UtcNow,
                Reason = "Orphan reviewer decision must be rejected."
            };
            db.ResultRescrutinyEvents.Add(orphan);
            await db.SaveChangesAsync();
            Assert.Equal(409, (await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, PageSize = 10,
                Status = ResultRescrutinyAction.Requested
            })).StatusCode);
            Assert.Equal(409, (await service.GetResultRescrutinySummaryAsync(publicationId)).StatusCode);
            Assert.Equal(409, (await service.ExportResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, PageSize = 50
            })).StatusCode);
            // Test-only cleanup uses exact synthetic tenant/row SQL: the EF
            // history guard intentionally rejects deletes of immutable events.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [ResultRescrutinyEvents] WHERE [TenantId] = {seeded.TenantId} AND [Id] = {orphan.Id}");
            Assert.True((await service.GetResultRescrutinySummaryAsync(publicationId)).Success);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db, new Actor(seeded.TenantId + 1, seeded.ReviewerId, "TenantAdmin"));
            Assert.Equal(404, (await foreign.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId
            })).StatusCode);
            Assert.Equal(404, (await foreign.GetResultRescrutinySummaryAsync(publicationId)).StatusCode);
            Assert.Equal(404, (await foreign.ExportResultRescrutinyAsync(new()
            {
                PublicationId = publicationId
            })).StatusCode);
            Assert.Equal(404, (await foreign.GetResultRescrutinyHistoryAsync(caseReference)).StatusCode);
            Assert.Equal(404, (await foreign.GetResultRescrutinyReviewAsync(caseReference)).StatusCode);
            Assert.Equal(404, (await foreign.DecideResultRescrutinyAsync(caseReference, new()
            {
                ClientRequestId = Guid.NewGuid(), RequestRowVersion = version,
                Decision = ResultRescrutinyAction.Rejected,
                Reason = "This is a foreign case."
            })).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            Assert.Equal(403, (await service.DecideResultRescrutinyAsync(caseReference, new()
            {
                ClientRequestId = Guid.NewGuid(), RequestRowVersion = version,
                Decision = ResultRescrutinyAction.Rejected,
                Reason = "Self-review must be denied."
            })).StatusCode);
        }
        var decisionKey = Guid.NewGuid();
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "TenantAdmin"));
            var decision = new DecideResultRescrutinyDto
            {
                ClientRequestId = decisionKey, RequestRowVersion = version,
                Decision = ResultRescrutinyAction.AcceptedForCorrection,
                Reason = "Independent review accepted for correction investigation."
            };
            var approved = await service.DecideResultRescrutinyAsync(caseReference, decision);
            Assert.True(approved.Success, approved.Message);
            var replay = await service.DecideResultRescrutinyAsync(caseReference, decision);
            Assert.True(replay.Success, replay.Message);
            var applicant = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var intakeReplayAfterDecision = await applicant.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = requestKey, PublicationId = publicationId,
                StudentReference = studentReference, Reason = "Please verify the published mark."
            });
            Assert.True(intakeReplayAfterDecision.Success, intakeReplayAfterDecision.Message);
            Assert.Equal(caseReference, intakeReplayAfterDecision.Data!.CaseReference);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection, intakeReplayAfterDecision.Data.Status);
            Assert.Equal(decision.Reason, intakeReplayAfterDecision.Data.DecisionReason);
            Assert.Equal(seeded.TeacherId, intakeReplayAfterDecision.Data.DecidedByUserId);
            Assert.Equal(409, (await service.DecideResultRescrutinyAsync(caseReference, new()
            {
                ClientRequestId = Guid.NewGuid(), RequestRowVersion = version,
                Decision = ResultRescrutinyAction.Rejected,
                Reason = "Second decision must fail."
            })).StatusCode);
            var page = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId
            });
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection,
                Assert.Single(page.Data!.Items).Status);
            var workload = await service.GetResultRescrutinySummaryAsync(publicationId);
            Assert.True(workload.Success, workload.Message);
            Assert.Equal(1, workload.Data!.Total);
            Assert.Equal(0, workload.Data.Pending);
            Assert.Equal(1, workload.Data.AcceptedForCorrection);
            Assert.Null(workload.Data.OldestPendingAt);
            var export = await service.ExportResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.AcceptedForCorrection
            });
            Assert.True(export.Success, export.Message);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection,
                Assert.Single(export.Data!.Items).Status);
            var history = await service.GetResultRescrutinyHistoryAsync(caseReference);
            Assert.True(history.Success, history.Message);
            Assert.Equal(2, history.Data!.Events.Count);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection, history.Data.Events[1].Action);
            var reviewed = await service.GetResultRescrutinyReviewAsync(caseReference);
            Assert.True(reviewed.Success, reviewed.Message);
            Assert.Equal(ResultRescrutinyAction.AcceptedForCorrection, reviewed.Data!.Status);
            Assert.Equal(70m, reviewed.Data.ObtainedMarks);
            var accepted = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.AcceptedForCorrection
            });
            Assert.True(accepted.Success, accepted.Message);
            Assert.Single(accepted.Data!.Items);
            var pending = await service.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Requested
            });
            Assert.True(pending.Success, pending.Message);
            Assert.Empty(pending.Data!.Items);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            Assert.Equal(2, await db.ResultRescrutinyEvents.AsNoTracking()
                .CountAsync(x => x.CaseReference == caseReference));
            Assert.Equal(70m, (await db.StudentResultSummaries.AsNoTracking()
                .SingleAsync(x => x.ResultPublicationId == publicationId)).ObtainedMarks);
            var eventRow = await db.ResultRescrutinyEvents.SingleAsync(x =>
                x.CaseReference == caseReference && !x.IsTerminal);
            eventRow.Reason = "Tampering attempt";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }


    [Fact]
    public async Task Rescrutiny_mark_evidence_rejects_corrupted_working_marks_and_foreign_case_ids()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        Guid studentReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var enrollmentId = await db.StudentSubjectRegistrations.AsNoTracking()
                .Where(x => x.Id == seeded.RegistrationId)
                .Select(x => x.StudentEnrollmentId).SingleAsync();
            studentReference = await (from enrollment in db.StudentEnrollments.AsNoTracking()
                join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
                where enrollment.Id == enrollmentId
                select student.PublicId).SingleAsync();
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Published,
                PublishedAt = DateTime.UtcNow, PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = subject.AssessmentId, StudentEnrollmentId = enrollmentId,
                PublicationVersionNo = 1, TotalMarks = 100m, ObtainedMarks = 70m,
                Percentage = 70m, GPA = 3m, GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        Guid caseReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var created = await service.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
                StudentReference = studentReference,
                Reason = "Review the current subject and published aggregate evidence."
            });
            Assert.True(created.Success, created.Message);
            caseReference = created.Data!.CaseReference;
            var evidence = await service.GetResultRescrutinyMarkEvidenceAsync(caseReference);
            Assert.True(evidence.Success, evidence.Message);
            Assert.Equal(70m, evidence.Data!.PublishedObtainedMarks);
            Assert.Equal(75m, Assert.Single(evidence.Data.Subjects).CurrentObtainedMarks);
            Assert.True(evidence.Data.RetrievedAtUtc > DateTime.UnixEpoch);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db, new Actor(seeded.TenantId + 1, seeded.ReviewerId, "TenantAdmin"));
            Assert.Equal(404, (await foreign.GetResultRescrutinyMarkEvidenceAsync(caseReference)).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var changed = await db.StudentAssessmentMarks
                .Where(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsAbsent, true));
            Assert.Equal(1, changed);
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var invalid = await service.GetResultRescrutinyMarkEvidenceAsync(caseReference);
            Assert.Equal(409, invalid.StatusCode);
            Assert.Null(invalid.Data);
            Assert.Equal(70m, (await db.StudentResultSummaries.AsNoTracking()
                .SingleAsync(x => x.ResultPublicationId == publicationId)).ObtainedMarks);
        }
    }

    [Fact]
    public async Task Linked_student_and_guardian_can_apply_and_read_only_their_published_rescrutiny()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        Guid studentReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var enrollmentId = await db.StudentSubjectRegistrations.AsNoTracking()
                .Where(x => x.Id == seeded.RegistrationId)
                .Select(x => x.StudentEnrollmentId).SingleAsync();
            var enrollment = await db.StudentEnrollments.SingleAsync(x => x.Id == enrollmentId);
            var student = await db.Students.SingleAsync(x => x.Id == enrollment.StudentId);
            student.UserId = seeded.UnassignedId;
            studentReference = student.PublicId;
            var guardian = new Guardian
            {
                TenantId = seeded.TenantId, PersonId = student.PersonId,
                UserId = seeded.TeacherId, FullName = "Synthetic linked guardian"
            };
            db.Guardians.Add(guardian);
            await db.SaveChangesAsync();
            db.StudentGuardians.Add(new StudentGuardian
            {
                TenantId = seeded.TenantId, StudentId = student.Id,
                GuardianId = guardian.Id, RelationCode = "Parent"
            });
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Published, PublishedAt = DateTime.UtcNow,
                PublishedByUserId = seeded.ReviewerId,
                VisibleToStudent = true, VisibleToGuardian = true
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = subject.AssessmentId,
                StudentEnrollmentId = enrollment.Id, PublicationVersionNo = 1,
                TotalMarks = 100m, ObtainedMarks = 70m, Percentage = 70m,
                GPA = 3m, GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        var request = new RequestResultRescrutinyDto
        {
            ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
            StudentReference = studentReference,
            Reason = "Please review the published result for this student."
        };
        Guid caseReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var student = Service(db, new Actor(seeded.TenantId, seeded.UnassignedId, "Student"));
            var created = await student.RequestResultRescrutinyAsync(request);
            Assert.True(created.Success, created.Message);
            caseReference = created.Data!.CaseReference;
            var replay = await student.RequestResultRescrutinyAsync(request);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(caseReference, replay.Data!.CaseReference);
            var page = await student.SearchLinkedResultRescrutinyAsync(studentReference, publicationId, null);
            Assert.True(page.Success, page.Message);
            Assert.Equal(caseReference, Assert.Single(page.Data!.Items).CaseReference);
            Assert.Equal(ResultRescrutinyAction.Requested, page.Data.Items[0].Status);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var guardian = Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "Guardian"));
            var page = await guardian.SearchLinkedResultRescrutinyAsync(studentReference, publicationId, null);
            Assert.True(page.Success, page.Message);
            Assert.Equal(caseReference, Assert.Single(page.Data!.Items).CaseReference);
            var duplicate = await guardian.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
                StudentReference = studentReference, Reason = "A second application is not allowed."
            });
            Assert.Equal(409, duplicate.StatusCode);
            var stranger = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "Guardian"));
            Assert.Equal(404, (await stranger.SearchLinkedResultRescrutinyAsync(
                studentReference, publicationId, null)).StatusCode);
            Assert.Equal(404, (await stranger.RequestResultRescrutinyAsync(new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationId = publicationId,
                StudentReference = studentReference, Reason = "Unlinked guardian attempt must be denied."
            })).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db, new Actor(seeded.TenantId + 1, seeded.UnassignedId, "Student"));
            Assert.Equal(404, (await foreign.SearchLinkedResultRescrutinyAsync(
                studentReference, publicationId, null)).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var administrator = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var pending = await administrator.SearchResultRescrutinyAsync(new()
            {
                PublicationId = publicationId, Status = ResultRescrutinyAction.Requested
            });
            Assert.Equal(caseReference, Assert.Single(pending.Data!.Items).CaseReference);
            Assert.Equal(70m, (await db.StudentResultSummaries.AsNoTracking()
                .SingleAsync(x => x.ResultPublicationId == publicationId)).ObtainedMarks);
        }
    }

    [Fact]
    public async Task Teacher_submission_rejection_correction_resubmission_and_independent_approval_are_atomic()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        var tenant = seeded.TenantId;
        var subjectId = seeded.AssessmentSubjectId;
        var teacher = new Actor(tenant, seeded.TeacherId, "Teacher");
        var reviewer = new Actor(tenant, seeded.ReviewerId, "TenantAdmin");
        var stranger = new Actor(tenant, seeded.UnassignedId, "Teacher");

        await using (var db = TenantDb(options, tenant))
        {
            var unassigned = await Service(db, stranger).GetMarkReviewAsync(subjectId);
            Assert.Equal(403, unassigned.StatusCode);
        }
        await using (var db = TenantDb(options, tenant + 1))
        {
            var foreign = await Service(db, new Actor(tenant + 1, seeded.ReviewerId, "TenantAdmin"))
                .GetMarkReviewAsync(subjectId);
            Assert.Equal(404, foreign.StatusCode);
        }

        var submissionKey = Guid.NewGuid();
        string initialAssessmentVersion;
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var before = await service.GetMarkReviewAsync(subjectId);
            Assert.True(before.Success, before.Message);
            Assert.True(before.Data!.CanSubmit);
            initialAssessmentVersion = before.Data.AssessmentRowVersion;
            var submitted = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.True(submitted.Success, submitted.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, submitted.Data!.CurrentDecision);
            Assert.False(submitted.Data.CanSubmit);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var replay = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.True(replay.Success, replay.Message);
            var changedPayload = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Rejected, "Altered"));
            // Authorization rejects a teacher trying to forge a reviewer decision before replay checks.
            Assert.Equal(403, changedPayload.StatusCode);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var blocked = await service.SaveMarksRegisterAsync(MarkChange(
                subjectId, seeded.RegistrationId, mark, 80m));
            Assert.Equal(409, blocked.StatusCode);
            var selfApproval = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), (await service.GetMarkReviewAsync(subjectId)).Data!
                    .AssessmentRowVersion, AssessmentMarkReviewDecision.Approved));
            Assert.Equal(403, selfApproval.StatusCode);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, reviewer);
            var pending = await service.GetMarkReviewAsync(subjectId);
            Assert.True(pending.Success, pending.Message);
            Assert.True(pending.Data!.CanReview);
            var reusedTeacherKey = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Rejected, "Forged review"));
            Assert.Equal(409, reusedTeacherKey.StatusCode);
            var rejected = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Rejected, "Component totals require checking"));
            Assert.True(rejected.Success, rejected.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Rejected, rejected.Data!.CurrentDecision);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var corrected = await service.SaveMarksRegisterAsync(
                MarkChange(subjectId, seeded.RegistrationId, mark, 80m));
            Assert.True(corrected.Success, corrected.Message);
        }
        var resubmissionKey = Guid.NewGuid();
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var correctedReview = await service.GetMarkReviewAsync(subjectId);
            Assert.True(correctedReview.Data!.CanSubmit);
            var resubmitted = await service.DecideMarkReviewAsync(subjectId,
                Decision(resubmissionKey, correctedReview.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.True(resubmitted.Success, resubmitted.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, resubmitted.Data!.CurrentDecision);
            // Old request keys cannot acknowledge a superseded decision.
            var obsolete = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.Equal(409, obsolete.StatusCode);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, reviewer);
            var pending = await service.GetMarkReviewAsync(subjectId);
            Assert.True(pending.Data!.CanReview);
            var stale = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), initialAssessmentVersion, AssessmentMarkReviewDecision.Approved));
            Assert.Equal(409, stale.StatusCode);
            var approved = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Approved));
            Assert.True(approved.Success, approved.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Approved, approved.Data!.CurrentDecision);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var frozen = await service.SaveMarksRegisterAsync(
                MarkChange(subjectId, seeded.RegistrationId, mark, 90m));
            Assert.Equal(409, frozen.StatusCode);
            var status = await service.GetMarkReviewAsync(subjectId);
            Assert.False(status.Data!.CanSubmit);
            Assert.False(status.Data.CanReview);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var decisions = await db.AssessmentMarkReviewEvents.AsNoTracking()
                .Where(x => x.TenantId == tenant && x.AssessmentSubjectId == subjectId)
                .OrderBy(x => x.Id).Select(x => x.Decision).ToArrayAsync();
            Assert.Equal(new[] {
                AssessmentMarkReviewDecision.Submitted, AssessmentMarkReviewDecision.Rejected,
                AssessmentMarkReviewDecision.Submitted, AssessmentMarkReviewDecision.Approved
            }, decisions);
            Assert.Equal(80m, (await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId)).ObtainedMarks);
            Assert.Equal(4, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.AssessmentSubjectId == subjectId));
        }
    }

    [Fact]
    public async Task Teacher_submission_reconciles_actual_component_values_before_persisting_review()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var first = new AssessmentComponent {
                TenantId = seeded.TenantId, AssessmentSubjectId = seeded.AssessmentSubjectId,
                Name = "Written", Code = "WRITTEN", FullMarks = 100m, Weightage = 50m
            };
            var second = new AssessmentComponent {
                TenantId = seeded.TenantId, AssessmentSubjectId = seeded.AssessmentSubjectId,
                Name = "Practical", Code = "PRACTICAL", FullMarks = 100m, Weightage = 50m
            };
            db.AssessmentComponents.AddRange(first, second);
            await db.SaveChangesAsync();
            var master = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId);
            db.StudentAssessmentComponentMarks.AddRange(
                new StudentAssessmentComponentMark {
                    TenantId = seeded.TenantId, AssessmentComponentId = first.Id,
                    StudentAssessmentMarkId = master.Id, ObtainedMarks = 60m,
                    EnteredByUserId = seeded.TeacherId
                },
                new StudentAssessmentComponentMark {
                    TenantId = seeded.TenantId, AssessmentComponentId = second.Id,
                    StudentAssessmentMarkId = master.Id, ObtainedMarks = 80m,
                    EnteredByUserId = seeded.TeacherId
                });
            await db.SaveChangesAsync();
        }
        var teacher = new Actor(seeded.TenantId, seeded.TeacherId, "Teacher");
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var review = await service.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            Assert.True(review.Success, review.Message);
            var invalid = await service.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                Decision(Guid.NewGuid(), review.Data!.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.Equal(409, invalid.StatusCode);
            Assert.Empty(await db.AssessmentMarkReviewEvents.AsNoTracking().ToListAsync());
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var part = await db.StudentAssessmentComponentMarks.SingleAsync(x =>
                x.ObtainedMarks == 80m);
            part.ObtainedMarks = 90m;
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var review = await service.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            var valid = await service.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                Decision(Guid.NewGuid(), review.Data!.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.True(valid.Success, valid.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, valid.Data!.CurrentDecision);
            Assert.Single(await db.AssessmentMarkReviewEvents.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task Historical_grade_distribution_and_version_comparison_execute_on_real_sql_with_tenant_guards()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long baselineId;
        long targetId;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var registration = await db.StudentSubjectRegistrations.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.RegistrationId);
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var baseline = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Withdrawn,
                PublishedAt = DateTime.UtcNow.AddMinutes(-10),
                PublishedByUserId = seeded.ReviewerId
            };
            var target = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 2,
                State = ResultPublicationState.Published,
                PublishedAt = DateTime.UtcNow,
                PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.AddRange(baseline, target);
            await db.SaveChangesAsync();
            baselineId = baseline.Id;
            targetId = target.Id;
            db.StudentResultSummaries.AddRange(
                new StudentResultSummary
                {
                    TenantId = seeded.TenantId, ResultPublicationId = baselineId,
                    AssessmentId = subject.AssessmentId,
                    StudentEnrollmentId = registration.StudentEnrollmentId,
                    PublicationVersionNo = 1, TotalMarks = 100m, ObtainedMarks = 75m,
                    Percentage = 75m, GPA = 3m, GradeLetter = "   ", IsPassed = true
                },
                new StudentResultSummary
                {
                    TenantId = seeded.TenantId, ResultPublicationId = targetId,
                    AssessmentId = subject.AssessmentId,
                    StudentEnrollmentId = registration.StudentEnrollmentId,
                    PublicationVersionNo = 2, TotalMarks = 100m, ObtainedMarks = 90m,
                    Percentage = 90m, GPA = 4m, GradeLetter = " A ", IsPassed = true
                });
            await db.SaveChangesAsync();
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var baseline = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = baselineId });
            Assert.True(baseline.Success, baseline.Message);
            Assert.Equal(1, baseline.Data!.StudentCount);
            Assert.Equal(1, baseline.Data.PassedCount);
            Assert.Equal("Unassigned", Assert.Single(baseline.Data.Grades).GradeLetter);
            Assert.Equal(75m, baseline.Data.Grades[0].TotalObtainedMarks);

            var target = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = targetId });
            Assert.True(target.Success, target.Message);
            Assert.Equal(90m, Assert.Single(target.Data!.Grades).TotalObtainedMarks);

            var comparison = await service.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = baselineId, TargetPublicationId = targetId });
            Assert.True(comparison.Success, comparison.Message);
            Assert.Equal(0, comparison.Data!.StudentCountChange);
            Assert.Equal(-1, comparison.Data.Grades.Single(x =>
                x.GradeLetter == "Unassigned").CountChange);
            var added = comparison.Data.Grades.Single(x => x.GradeLetter == "A");
            Assert.Equal(1, added.CountChange);
            Assert.Equal(90m, added.ObtainedMarksChange);
            Assert.Null(added.AveragePercentageChange);

            var reversed = await service.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = targetId, TargetPublicationId = baselineId });
            Assert.Equal(409, reversed.StatusCode);
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var teacher = Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "Teacher"));
            Assert.Equal(403, (await teacher.GetPublishedGradeDistributionAsync(
                new() { PublicationId = baselineId })).StatusCode);
            Assert.Equal(403, (await teacher.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = baselineId,
                    TargetPublicationId = targetId })).StatusCode);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db,
                new Actor(seeded.TenantId + 1, seeded.ReviewerId, "TenantAdmin"));
            Assert.Equal(404, (await foreign.GetPublishedGradeDistributionAsync(
                new() { PublicationId = targetId })).StatusCode);
            Assert.Equal(404, (await foreign.ComparePublishedGradeDistributionsAsync(
                new() { BaselinePublicationId = baselineId,
                    TargetPublicationId = targetId })).StatusCode);
        }
    }

    [Fact]
    public async Task Historical_grade_report_fails_closed_when_finalized_snapshot_is_soft_deleted()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.AsNoTracking()
                .SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var enrollmentId = await db.StudentSubjectRegistrations.AsNoTracking()
                .Where(x => x.Id == seeded.RegistrationId)
                .Select(x => x.StudentEnrollmentId).SingleAsync();
            var batchId = await db.SubjectOfferings.AsNoTracking()
                .Where(x => x.Id == subject.SubjectOfferingId)
                .Select(x => x.AcademicBatchId).SingleAsync();
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = subject.AssessmentId,
                AcademicBatchId = batchId, VersionNo = 1,
                State = ResultPublicationState.Published,
                PublishedAt = DateTime.UtcNow, PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = subject.AssessmentId, StudentEnrollmentId = enrollmentId,
                PublicationVersionNo = 1, TotalMarks = 100m, ObtainedMarks = 75m,
                Percentage = 75m, GPA = 3m, GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db,
                new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"));
            var valid = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = publicationId });
            Assert.True(valid.Success, valid.Message);

            // Simulate corrupted historical evidence via SQL, deliberately bypassing
            // the EF append-only guard in this disposable integration database.
            var affected = await db.StudentResultSummaries
                .Where(x => x.ResultPublicationId == publicationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDeleted, true));
            Assert.Equal(1, affected);
            var denied = await service.GetPublishedGradeDistributionAsync(
                new() { PublicationId = publicationId });
            Assert.Equal(409, denied.StatusCode);
            Assert.Null(denied.Data);
        }
    }


    [Fact]
    public async Task Competing_review_approvals_commit_once_and_lock_requires_the_winning_review()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        var teacher = new Actor(seeded.TenantId, seeded.TeacherId, "Teacher");
        var reviewer = new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin");
        Guid examReference;

        // The lock calculation uses the canonical grade scheme, not a test-only bypass.
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var exam = await db.Assessments.SingleAsync(x => x.Id == subject.AssessmentId);
            examReference = exam.PublicId;
            var scheme = new GradeScheme
            {
                TenantId = seeded.TenantId, Name = "Concurrent review grading",
                Code = "REVIEW-" + Guid.NewGuid().ToString("N")[..12]
            };
            db.GradeSchemes.Add(scheme);
            await db.SaveChangesAsync();
            db.GradeRules.Add(new GradeRule
            {
                TenantId = seeded.TenantId, GradeSchemeId = scheme.Id,
                MinMarks = 0m, MaxMarks = 100m, GradeLetter = "A",
                GradePoint = 4m, DisplayOrder = 1
            });
            exam.GradeSchemeId = scheme.Id;
            await db.SaveChangesAsync();
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var review = await service.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            Assert.True(review.Success, review.Message);
            var submitted = await service.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                Decision(Guid.NewGuid(), review.Data!.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.True(submitted.Success, submitted.Message);
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, reviewer);
            var exam = await service.GetAssessmentAsync(examReference);
            Assert.True(exam.Success, exam.Message);
            var prematureLock = await service.ChangeAssessmentStateAsync(examReference,
                new ChangeAssessmentStateRequestDto
                {
                    State = AssessmentState.Locked, RowVersion = exam.Data!.RowVersion
                });
            Assert.Equal(409, prematureLock.StatusCode);
        }

        await using (var firstDb = TenantDb(options, seeded.TenantId))
        await using (var secondDb = TenantDb(options, seeded.TenantId))
        {
            var first = Service(firstDb, reviewer);
            var second = Service(secondDb, reviewer);
            var firstView = await first.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            var secondView = await second.GetMarkReviewAsync(seeded.AssessmentSubjectId);
            Assert.True(firstView.Data!.CanReview);
            Assert.True(secondView.Data!.CanReview);

            var outcomes = await Task.WhenAll(
                first.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                    Decision(Guid.NewGuid(), firstView.Data.AssessmentRowVersion,
                        AssessmentMarkReviewDecision.Approved)),
                second.DecideMarkReviewAsync(seeded.AssessmentSubjectId,
                    Decision(Guid.NewGuid(), secondView.Data.AssessmentRowVersion,
                        AssessmentMarkReviewDecision.Approved)));
            Assert.Single(outcomes.Where(x => x.Success));
            Assert.Single(outcomes.Where(x => x.StatusCode == 409));
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            Assert.Equal(2, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId));
            Assert.Equal(1, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId &&
                    x.Decision == AssessmentMarkReviewDecision.Approved));
            var service = Service(db, reviewer);
            var exam = await service.GetAssessmentAsync(examReference);
            Assert.True(exam.Success, exam.Message);
            var locked = await service.ChangeAssessmentStateAsync(examReference,
                new ChangeAssessmentStateRequestDto
                {
                    State = AssessmentState.Locked, RowVersion = exam.Data!.RowVersion
                });
            Assert.True(locked.Success, locked.Message);
            Assert.Equal(AssessmentState.Locked, locked.Data!.State);
        }

        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId);
            var edit = await service.SaveMarksRegisterAsync(
                MarkChange(seeded.AssessmentSubjectId, seeded.RegistrationId, mark, 85m));
            Assert.Equal(409, edit.StatusCode);
            Assert.Equal(75m, (await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.Id == mark.Id)).ObtainedMarks);
        }
    }

    private static AssessmentMarkReviewRequestDto Decision(Guid key, string version,
        AssessmentMarkReviewDecision decision, string? reason = null) => new()
    {
        ClientRequestId = key, AssessmentRowVersion = version,
        Decision = decision, Reason = reason
    };

    private static SaveMarksRegisterRequestDto MarkChange(long subjectId, long registrationId,
        StudentAssessmentMark mark, decimal marks) => new()
    {
        AssessmentSubjectId = subjectId,
        Marks = [new SaveStudentAssessmentMarkRequestDto
        {
            StudentSubjectRegistrationId = registrationId, ObtainedMarks = marks,
            RowVersion = Convert.ToBase64String(mark.RowVersion)
        }]
    };

    [Fact]
    public async Task Accepted_rescrutiny_requires_independent_correction_and_retains_both_published_versions()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        long publicationId;
        Guid studentReference;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var subject = await db.AssessmentSubjects.SingleAsync(x => x.Id == seeded.AssessmentSubjectId);
            var exam = await db.Assessments.SingleAsync(x => x.Id == subject.AssessmentId);
            var scheme = new GradeScheme
            {
                TenantId = seeded.TenantId, Code = "SCHEME-" + Guid.NewGuid().ToString("N")[..8],
                Name = "Synthetic examination grade", IsActive = true
            };
            db.GradeSchemes.Add(scheme);
            await db.SaveChangesAsync();
            db.GradeRules.Add(new GradeRule
            {
                TenantId = seeded.TenantId, GradeSchemeId = scheme.Id,
                GradeLetter = "B", GradePoint = 3m, MinMarks = 0m, MaxMarks = 100m
            });
            exam.GradeSchemeId = scheme.Id;
            exam.State = AssessmentState.Locked;
            await db.SaveChangesAsync();
            var registration = await db.StudentSubjectRegistrations.SingleAsync(x => x.Id == seeded.RegistrationId);
            var enrollment = await db.StudentEnrollments.SingleAsync(x => x.Id == registration.StudentEnrollmentId);
            studentReference = await db.Students.AsNoTracking().Where(x => x.Id == enrollment.StudentId)
                .Select(x => x.PublicId).SingleAsync();
            var offering = await db.SubjectOfferings.SingleAsync(x => x.Id == subject.SubjectOfferingId);
            var publication = new ResultPublication
            {
                TenantId = seeded.TenantId, AssessmentId = exam.Id,
                AcademicBatchId = offering.AcademicBatchId, VersionNo = 1,
                State = ResultPublicationState.Published, PublishedAt = DateTime.UtcNow,
                PublishedByUserId = seeded.ReviewerId
            };
            db.ResultPublications.Add(publication);
            await db.SaveChangesAsync();
            publicationId = publication.Id;
            db.StudentResultSummaries.Add(new StudentResultSummary
            {
                TenantId = seeded.TenantId, ResultPublicationId = publicationId,
                AssessmentId = exam.Id, StudentEnrollmentId = enrollment.Id,
                PublicationVersionNo = 1, TotalMarks = 100m,
                ObtainedMarks = 75m, Percentage = 75m, GPA = 3m,
                GradeLetter = "B", IsPassed = true
            });
            await db.SaveChangesAsync();
        }
        Guid caseReference;
        string requestVersion;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var intake = await Service(db, new Actor(seeded.TenantId, seeded.ReviewerId, "TenantAdmin"))
                .RequestResultRescrutinyAsync(new()
                {
                    PublicationId = publicationId, ClientRequestId = Guid.NewGuid(),
                    StudentReference = studentReference, Reason = "Correct an independently reviewed mark."
                });
            Assert.True(intake.Success, intake.Message);
            caseReference = intake.Data!.CaseReference;
            requestVersion = intake.Data.RequestRowVersion;
        }
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var decision = await Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "TenantAdmin"))
                .DecideResultRescrutinyAsync(caseReference, new()
                {
                    ClientRequestId = Guid.NewGuid(), RequestRowVersion = requestVersion,
                    Decision = ResultRescrutinyAction.AcceptedForCorrection,
                    Reason = "Correction approved after independent evidence review."
                });
            Assert.True(decision.Success, decision.Message);
        }
        ApplyResultCorrectionRequestDto amendment;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var reviewer = Service(db, new Actor(seeded.TenantId, seeded.TeacherId, "TenantAdmin"));
            var marks = await reviewer.GetResultRescrutinyMarkEvidenceAsync(caseReference);
            Assert.True(marks.Success, marks.Message);
            var subject = Assert.Single(marks.Data!.Subjects);
            amendment = new ApplyResultCorrectionRequestDto
            {
                ClientRequestId = Guid.NewGuid(),
                PublicationRowVersion = marks.Data.PublicationRowVersion,
                Reason = "Independent correction to the verified marks register.",
                Subjects = [new ResultCorrectionSubjectInputDto
                {
                    AssessmentSubjectId = subject.AssessmentSubjectId,
                    RowVersion = subject.MarkRowVersion!, ObtainedMarks = 85m
                }]
            };
            var selfCorrection = await reviewer.ApplyResultCorrectionAsync(caseReference, amendment);
            Assert.Equal(403, selfCorrection.StatusCode);
        }
        long latestId;
        await using (var db = TenantDb(options, seeded.TenantId))
        {
            var executor = Service(db, new Actor(seeded.TenantId, seeded.UnassignedId, "TenantAdmin"));
            var invalid = new ApplyResultCorrectionRequestDto
            {
                ClientRequestId = Guid.NewGuid(), PublicationRowVersion = amendment.PublicationRowVersion,
                Reason = amendment.Reason,
                Subjects = [new ResultCorrectionSubjectInputDto
                {
                    AssessmentSubjectId = seeded.AssessmentSubjectId,
                    RowVersion = amendment.Subjects[0].RowVersion, ObtainedMarks = 101m
                }]
            };
            Assert.Equal(409, (await executor.ApplyResultCorrectionAsync(caseReference, invalid)).StatusCode);
            var applied = await executor.ApplyResultCorrectionAsync(caseReference, amendment);
            Assert.True(applied.Success, applied.Message);
            Assert.Equal(1, applied.Data!.PreviousVersionNo);
            Assert.Equal(2, applied.Data.NewVersionNo);
            latestId = applied.Data.NewPublicationId;
            var replay = await executor.ApplyResultCorrectionAsync(caseReference, amendment);
            Assert.True(replay.Success, replay.Message);
            Assert.Equal(latestId, replay.Data!.NewPublicationId);
            var reusedKey = await executor.ApplyResultCorrectionAsync(caseReference, new()
            {
                ClientRequestId = amendment.ClientRequestId,
                PublicationRowVersion = amendment.PublicationRowVersion,
                Reason = "A different unauthorized request under the same idempotency key.",
                Subjects = amendment.Subjects
            });
            Assert.Equal(409, reusedKey.StatusCode);
            var correction = await executor.GetResultCorrectionAsync(caseReference);
            Assert.True(correction.Success, correction.Message);
            Assert.Equal(latestId, correction.Data!.NewPublicationId);
            var old = await db.ResultPublications.AsNoTracking().SingleAsync(x => x.Id == publicationId);
            Assert.Equal(ResultPublicationState.Withdrawn, old.State);
            Assert.False(old.VisibleToStudent);
            Assert.False(old.VisibleToGuardian);
            var next = await db.ResultPublications.AsNoTracking().SingleAsync(x => x.Id == latestId);
            Assert.Equal(ResultPublicationState.Published, next.State);
            Assert.Equal(2, next.VersionNo);
            var historic = await db.StudentResultSummaries.AsNoTracking()
                .Where(x => x.ResultPublicationId == publicationId).SingleAsync();
            var corrected = await db.StudentResultSummaries.AsNoTracking()
                .Where(x => x.ResultPublicationId == latestId).SingleAsync();
            Assert.Equal(75m, historic.ObtainedMarks);
            Assert.Equal(85m, corrected.ObtainedMarks);
            Assert.Equal(1, await db.ResultCorrectionEvents.AsNoTracking()
                .CountAsync(x => x.CaseReference == caseReference));
            var subjectMark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == seeded.AssessmentSubjectId);
            Assert.Equal(85m, subjectMark.ObtainedMarks);
        }
        await using (var db = TenantDb(options, seeded.TenantId + 1))
        {
            var foreign = Service(db, new Actor(seeded.TenantId + 1, seeded.UnassignedId, "TenantAdmin"));
            Assert.Equal(404, (await foreign.GetResultCorrectionAsync(caseReference)).StatusCode);
            Assert.Equal(404, (await foreign.ApplyResultCorrectionAsync(caseReference, new()
            {
                ClientRequestId = Guid.NewGuid(), PublicationRowVersion = amendment.PublicationRowVersion,
                Reason = amendment.Reason, Subjects = amendment.Subjects
            })).StatusCode);
        }
    }

    private static AssessmentAdministrationService Service(EduOSDbContext db, Actor actor)
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
            Repo<ResultCorrectionEvent>(), Repo<StudentResultCourseSnapshot>());
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private static async Task<Seeded> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new EduOSDbContext(options);
        var tenant = new Tenant { Code = "CI-EXFLOW-" + suffix, Name = "Review lifecycle",
            Email = "exflow-" + suffix + "@example.test", State = TenantState.Active };
        ApplicationUser User(string role) {
            var name = "exflow-" + role + "-" + suffix + "@example.test";
            return new ApplicationUser { UserName = name,
                NormalizedUserName = name.ToUpperInvariant(), Email = name,
                NormalizedEmail = name.ToUpperInvariant(), FullName = role,
                IsActive = true, EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
        }
        var teacher = User("teacher");
        var reviewer = User("reviewer");
        var unassigned = User("unassigned");
        var teacherPerson = new Person { FullName = "Synthetic teacher" };
        var studentPerson = new Person { FullName = "Synthetic student" };
        setup.AddRange(tenant, teacher, reviewer, unassigned, teacherPerson, studentPerson);
        await setup.SaveChangesAsync();
        var tenantId = tenant.Id;
        setup.ChangeTracker.Clear();
        using (setup.BeginSystemTenantScope(tenantId))
        {
            var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
            var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
            var program = new AcademicProgram { TenantId = tenantId, Name = "School", Code = "SCH" };
            var subject = new Subject { TenantId = tenantId, Name = "Math", Code = "MATH" };
            var designation = new Designation { TenantId = tenantId, Name = "Teacher", Code = "TCHR" };
            var student = new Student { TenantId = tenantId, PersonId = studentPerson.Id,
                FullName = "Synthetic student", StudentCode = "S-" + suffix,
                StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) };
            setup.AddRange(campus, year, program, subject, designation, student);
            await setup.SaveChangesAsync();
            var employee = new Employee { TenantId = tenantId, PersonId = teacherPerson.Id,
                UserId = teacher.Id, DesignationId = designation.Id, EmployeeCode = "T-" + suffix,
                FullName = "Synthetic teacher", CanTeach = true, State = EmployeeState.Active,
                JoiningDate = new DateOnly(2026, 1, 1) };
            var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                Name = "One", Code = "ONE", LevelNo = 1 };
            var curriculum = new AcademicCurriculum { TenantId = tenantId,
                AcademicProgramId = program.Id, Name = "Curriculum", Code = "CUR",
                EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
            setup.AddRange(employee, level, curriculum);
            await setup.SaveChangesAsync();
            var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                AcademicYearId = year.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, Name = "Class One", Code = "C1" };
            var curriculumSubject = new CurriculumSubject { TenantId = tenantId,
                AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
                SubjectId = subject.Id };
            setup.AddRange(batch, curriculumSubject);
            await setup.SaveChangesAsync();
            var offering = new SubjectOffering { TenantId = tenantId,
                AcademicBatchId = batch.Id, AcademicYearId = year.Id,
                CurriculumSubjectId = curriculumSubject.Id, Code = "MATH" };
            var exam = new Assessment { TenantId = tenantId, CampusId = campus.Id,
                AcademicYearId = year.Id, Name = "Final", Code = "FINAL",
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 2),
                State = AssessmentState.MarksEntry };
            var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                AcademicYearId = year.Id, CampusId = campus.Id,
                AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                AcademicBatchId = batch.Id, AcademicCurriculumId = curriculum.Id,
                RollNo = "01", EnrollmentDate = new DateOnly(2026, 1, 1),
                State = EnrollmentState.Active, IsCurrent = true, ClientRequestId = Guid.NewGuid() };
            setup.AddRange(offering, exam, enrollment);
            await setup.SaveChangesAsync();
            var assessmentSubject = new AssessmentSubject { TenantId = tenantId,
                AssessmentId = exam.Id, SubjectOfferingId = offering.Id };
            var registration = new StudentSubjectRegistration { TenantId = tenantId,
                StudentEnrollmentId = enrollment.Id, SubjectOfferingId = offering.Id,
                ClientRequestId = Guid.NewGuid(), State = SubjectRegistrationState.Approved };
            var assignment = new InstructorAssignment { TenantId = tenantId,
                SubjectOfferingId = offering.Id, EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
            setup.AddRange(assessmentSubject, registration, assignment);
            await setup.SaveChangesAsync();
            var mark = new StudentAssessmentMark { TenantId = tenantId,
                AssessmentSubjectId = assessmentSubject.Id,
                StudentSubjectRegistrationId = registration.Id, ObtainedMarks = 75m,
                EnteredByUserId = teacher.Id, EnteredAt = DateTime.UtcNow };
            setup.StudentAssessmentMarks.Add(mark);
            await setup.SaveChangesAsync();
            return new Seeded(tenantId, teacher.Id, reviewer.Id, unassigned.Id,
                assessmentSubject.Id, registration.Id);
        }
    }

    private sealed record Seeded(long TenantId, long TeacherId, long ReviewerId,
        long UnassignedId, long AssessmentSubjectId, long RegistrationId);

    private sealed class Actor(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "SQL exam test";
        public string? Email => "sql-exam@example.test";
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL exam lifecycle";
    }
}
