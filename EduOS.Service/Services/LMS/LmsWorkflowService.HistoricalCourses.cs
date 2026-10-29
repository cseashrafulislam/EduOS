using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    // Archived courses are never discoverable from a bare course-enrollment record.
    // Require the original linked academic enrollment, matching campus/batch/year,
    // curriculum effective on the enrollment date, and a completed LMS enrollment.
    // Reuse the exact same historical authorization predicate for course discovery,
    // direct-ID reads and enrollment-specific progress. No unbounded ID materialization.
    private IQueryable<long>? HistoricalCourseIdsQuery(long studentId) =>
        HistoricalCourseEnrollmentsQuery(studentId)?.Select(x => x.CourseId).Distinct();

    private IQueryable<long>? HistoricalCourseEnrollmentIdsQuery(long studentId) =>
        HistoricalCourseEnrollmentsQuery(studentId)?.Select(x => x.Id).Distinct();

    private IQueryable<CourseEnrollment>? HistoricalCourseEnrollmentsQuery(long studentId)
    {
        if (studentId <= 0 || _submissionBatches == null || _submissionYears == null ||
            _submissionCurricula == null || _submissionTerms == null)
            return null;
        var tenant = _user.TenantId;
        var now = _clock.GetUtcNow().UtcDateTime;
        var query =
            from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join course in _courses.GetQueryable().AsNoTracking()
                on enrollment.CourseId equals course.Id
            join academic in _academicEnrollments.GetQueryable().AsNoTracking()
                on enrollment.StudentEnrollmentId equals (long?)academic.Id
            join batch in _submissionBatches.GetQueryable().AsNoTracking()
                on academic.AcademicBatchId equals batch.Id
            join year in _submissionYears.GetQueryable().AsNoTracking()
                on academic.AcademicYearId equals year.Id
            join curriculum in _submissionCurricula.GetQueryable().AsNoTracking()
                on academic.AcademicCurriculumId equals curriculum.Id
            where enrollment.TenantId == tenant && course.TenantId == tenant &&
                academic.TenantId == tenant && batch.TenantId == tenant &&
                year.TenantId == tenant && curriculum.TenantId == tenant &&
                !enrollment.IsDeleted && !course.IsDeleted && !academic.IsDeleted &&
                !batch.IsDeleted && !year.IsDeleted && !curriculum.IsDeleted &&
                enrollment.StudentId == studentId && academic.StudentId == studentId &&
                enrollment.State == CourseEnrollmentState.Completed &&
                enrollment.CompletedAt.HasValue && enrollment.CompletedAt.Value <= now &&
                enrollment.CompletedAt.Value >= enrollment.EnrolledAt &&
                !course.IsActive &&
                (academic.State == EnrollmentState.Completed ||
                 academic.State == EnrollmentState.Promoted ||
                 academic.State == EnrollmentState.Active) &&
                academic.EnrollmentDate >= year.StartDate &&
                academic.EnrollmentDate <= year.EndDate &&
                (!academic.EndDate.HasValue || academic.EndDate.Value >= academic.EnrollmentDate) &&
                batch.CampusId == academic.CampusId &&
                batch.AcademicYearId == academic.AcademicYearId &&
                batch.AcademicProgramId == academic.AcademicProgramId &&
                batch.AcademicLevelId == academic.AcademicLevelId &&
                (!batch.StartDate.HasValue || batch.StartDate.Value <= academic.EnrollmentDate) &&
                (!batch.EndDate.HasValue || batch.EndDate.Value >= academic.EnrollmentDate) &&
                (!year.CampusId.HasValue || year.CampusId == academic.CampusId) &&
                curriculum.AcademicProgramId == academic.AcademicProgramId &&
                curriculum.EffectiveFrom <= academic.EnrollmentDate &&
                (!curriculum.EffectiveTo.HasValue || curriculum.EffectiveTo.Value >= academic.EnrollmentDate) &&
                (!course.AcademicProgramId.HasValue ||
                 course.AcademicProgramId.Value == academic.AcademicProgramId) &&
                (!academic.AcademicTermId.HasValue ||
                 _submissionTerms.GetQueryable().Any(term =>
                    term.TenantId == tenant && !term.IsDeleted &&
                    term.Id == academic.AcademicTermId.Value &&
                    term.AcademicYearId == year.Id &&
                    term.StartDate <= year.EndDate && term.EndDate >= year.StartDate)) &&
                (!course.SubjectId.HasValue ||
                 _curriculumSubjects.GetQueryable().Any(subject =>
                    subject.TenantId == tenant && !subject.IsDeleted &&
                    subject.AcademicCurriculumId == curriculum.Id &&
                    subject.AcademicLevelId == academic.AcademicLevelId &&
                    subject.SubjectId == course.SubjectId.Value))
            select enrollment;
        return query;
    }
}
