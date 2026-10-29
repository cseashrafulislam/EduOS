using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    /// <summary>
    /// A bounded academic course preview backed exclusively by frozen active
    /// publication versions, never by editable marks or grade-rule recalculation.
    /// A formal printable transcript needs its own separately signed snapshot.
    /// </summary>
    public async Task<ApiResponse<TranscriptAcademicRecordDto>> GetTranscriptAcademicRecordAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<TranscriptAcademicRecordDto>("Academic record access requires examination administration.", 403);
        if (studentReference == Guid.Empty)
            return Error<TranscriptAcademicRecordDto>("Student reference is required.");
        if (_courseSnapshots == null)
            return Error<TranscriptAcademicRecordDto>("Course history storage is unavailable.", 503);

        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (student == null) return Error<TranscriptAcademicRecordDto>("Student not found.", 404);

        // Prevent a restricted campus manager from discovering another campus's
        // student history through a valid public student reference.
        if (IsRestrictedCampusManager())
        {
            var campuses = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.StudentId == student.Id && !x.IsDeleted)
                .Select(x => x.CampusId).Distinct().ToArrayAsync(ct);
            if (campuses.Length == 0)
                return Error<TranscriptAcademicRecordDto>("Student enrollment not found.", 404);
            foreach (var campus in campuses)
                if (!await CanAccessCampusAsync(campus, ct))
                    return Error<TranscriptAcademicRecordDto>("Campus access denied.", 403);
        }

        var published = await (
            from summary in _summaries.GetQueryable().AsNoTracking()
            join publication in _publications.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.ResultPublicationId }
                equals new { publication.TenantId, publication.Id }
            join assessment in _assessments.GetQueryable().AsNoTracking()
                on new { publication.TenantId, Id = publication.AssessmentId }
                equals new { assessment.TenantId, assessment.Id }
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { publication.TenantId, Id = publication.AcademicBatchId }
                equals new { batch.TenantId, batch.Id }
            where summary.TenantId == tenant && enrollment.StudentId == student.Id &&
                !summary.IsDeleted && !publication.IsDeleted && !assessment.IsDeleted &&
                !enrollment.IsDeleted && !batch.IsDeleted &&
                publication.State == ResultPublicationState.Published &&
                publication.VisibleToStudent && publication.PublishedAt != null &&
                summary.AssessmentId == assessment.Id &&
                summary.PublicationVersionNo == publication.VersionNo &&
                enrollment.AcademicBatchId == batch.Id &&
                enrollment.CampusId == batch.CampusId && assessment.CampusId == batch.CampusId &&
                assessment.AcademicYearId == enrollment.AcademicYearId && !summary.IsWithheld
            orderby assessment.EndDate, publication.Id
            select new
            {
                PublicationId = publication.Id, EnrollmentId = enrollment.Id,
                publication.VersionNo, publication.HasCourseGradeSnapshots,
                assessment.CountsTowardCgpa, AssessmentName = assessment.Name,
                assessment.EndDate, assessment.CampusId, assessment.AcademicYearId,
                BatchName = batch.Name, summary.CGPA, summary.CourseCount
            }).Take(501).ToListAsync(ct);
        if (published.Count == 0)
            return Error<TranscriptAcademicRecordDto>("No released published result exists for the student.", 404);
        if (published.Count > 500)
            return Error<TranscriptAcademicRecordDto>("Course history exceeds the bounded record limit.", 409);

        // Check entire versions, even though only one student's data will be
        // shown. A damaged class-wide snapshot must not be used as transcript evidence.
        foreach (var group in published.GroupBy(x => (x.CampusId, x.AcademicYearId)))
        {
            var proof = await GetPublicationEvidenceAsync(group.Select(x => x.PublicationId).Distinct().ToArray(),
                group.Key.CampusId, group.Key.AcademicYearId, ct);
            if (group.Any(x => !proof.TryGetValue(x.PublicationId, out var p) || !p.IsReconciled))
                return Error<TranscriptAcademicRecordDto>("Published course evidence needs reconciliation.", 409);
        }
        if (published.Any(x => x.CountsTowardCgpa && !x.HasCourseGradeSnapshots))
            return Error<TranscriptAcademicRecordDto>("Legacy cumulative results lack immutable course evidence.", 409);

        var scoped = published.Where(x => x.HasCourseGradeSnapshots).ToArray();
        var pubIds = scoped.Select(x => x.PublicationId).Distinct().ToArray();
        var enrollmentIds = scoped.Select(x => x.EnrollmentId).Distinct().ToArray();
        var courseRows = await (
            from course in _courseSnapshots.GetQueryable().AsNoTracking()
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on new { course.TenantId, Id = course.CurriculumSubjectId }
                equals new { curriculum.TenantId, curriculum.Id }
            join subject in _subjectNames.GetQueryable().AsNoTracking()
                on new { curriculum.TenantId, Id = curriculum.SubjectId }
                equals new { subject.TenantId, subject.Id }
            where course.TenantId == tenant && !course.IsDeleted &&
                pubIds.Contains(course.ResultPublicationId) &&
                enrollmentIds.Contains(course.StudentEnrollmentId) &&
                !curriculum.IsDeleted && !subject.IsDeleted
            select new { course, SubjectName = subject.Name, SubjectCode = subject.Code })
            .Take(2501).ToListAsync(ct);
        if (courseRows.Count > 2500 ||
            scoped.Any(x => courseRows.Count(c => c.course.ResultPublicationId == x.PublicationId &&
                c.course.StudentEnrollmentId == x.EnrollmentId) != x.CourseCount))
            return Error<TranscriptAcademicRecordDto>("Course names or publication evidence require reconciliation.", 409);

        var metadata = scoped.ToDictionary(x => (x.PublicationId, x.EnrollmentId));
        var items = courseRows.Select(x =>
        {
            var source = metadata[(x.course.ResultPublicationId, x.course.StudentEnrollmentId)];
            return new TranscriptAcademicCourseDto
            {
                PublicationId = source.PublicationId,
                PublicationVersionNo = source.VersionNo,
                AssessmentName = source.AssessmentName, AssessmentEndDate = source.EndDate,
                AcademicBatchName = source.BatchName,
                SubjectName = x.SubjectName, SubjectCode = x.SubjectCode,
                CreditHours = x.course.CreditHours, FullMarks = x.course.FullMarks,
                ObtainedMarks = x.course.ObtainedMarks,
                GradePoint = x.course.GradePoint, GradeLetter = x.course.GradeLetter,
                IsPassed = x.course.IsPassed, IsWithheld = x.course.IsWithheld
            };
        }).OrderBy(x => x.AssessmentEndDate).ThenBy(x => x.PublicationId)
          .ThenBy(x => x.SubjectCode).ToArray();
        var latest = published.Where(x => x.CountsTowardCgpa && x.CGPA.HasValue)
            .OrderByDescending(x => x.EndDate).ThenByDescending(x => x.PublicationId)
            .FirstOrDefault();
        return ApiResponse<TranscriptAcademicRecordDto>.SuccessResponse(new()
        {
            StudentReference = student.PublicId, StudentCode = student.StudentCode,
            StudentName = student.FullName, LatestPublishedCgpa = latest?.CGPA,
            LegacyPublishedResults = published.Count(x => !x.HasCourseGradeSnapshots),
            Courses = items
        });
    }
}
