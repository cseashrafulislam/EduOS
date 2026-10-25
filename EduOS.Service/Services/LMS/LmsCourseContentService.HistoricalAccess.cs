using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsCourseContentService
{
    // Archived content requires the original completed LMS enrollment AND its historical
    // academic enrollment; current campus membership cannot grant access retroactively.
    private async Task<bool> CanViewHistoricalAsync(Course course, long studentId, CancellationToken ct)
    {
        if (course.IsActive || studentId <= 0) return false;
        var tenant = Tenant;
        var now = Now;
        return await (
            from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join academic in _academicEnrollments.GetQueryable().AsNoTracking()
                on enrollment.StudentEnrollmentId equals (long?)academic.Id
            join batch in _batches.GetQueryable().AsNoTracking()
                on academic.AcademicBatchId equals batch.Id
            join year in _years.GetQueryable().AsNoTracking()
                on academic.AcademicYearId equals year.Id
            join curriculum in _curricula.GetQueryable().AsNoTracking()
                on academic.AcademicCurriculumId equals curriculum.Id
            where enrollment.TenantId == tenant && academic.TenantId == tenant &&
                batch.TenantId == tenant && year.TenantId == tenant && curriculum.TenantId == tenant &&
                !enrollment.IsDeleted && !academic.IsDeleted && !batch.IsDeleted &&
                !year.IsDeleted && !curriculum.IsDeleted &&
                enrollment.CourseId == course.Id && enrollment.StudentId == studentId &&
                academic.StudentId == studentId &&
                enrollment.State == CourseEnrollmentState.Completed &&
                enrollment.CompletedAt.HasValue && enrollment.CompletedAt.Value <= now &&
                enrollment.CompletedAt.Value >= enrollment.EnrolledAt &&
                (academic.State == EnrollmentState.Completed ||
                 academic.State == EnrollmentState.Promoted ||
                 academic.State == EnrollmentState.Active) &&
                academic.EnrollmentDate >= year.StartDate && academic.EnrollmentDate <= year.EndDate &&
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
                 _terms.GetQueryable().Any(term =>
                    term.TenantId == tenant && !term.IsDeleted &&
                    term.Id == academic.AcademicTermId.Value &&
                    term.AcademicYearId == year.Id &&
                    term.StartDate <= year.EndDate && term.EndDate >= year.StartDate)) &&
                (!course.SubjectId.HasValue ||
                 _subjects.GetQueryable().Any(subject =>
                    subject.TenantId == tenant && !subject.IsDeleted &&
                    subject.AcademicCurriculumId == curriculum.Id &&
                    subject.AcademicLevelId == academic.AcademicLevelId &&
                    subject.SubjectId == course.SubjectId.Value))
            select enrollment.Id
        ).AnyAsync(ct);
    }
}
