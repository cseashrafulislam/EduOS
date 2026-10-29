using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsWorkflowService
{
    // Only active enrollments from the already-authorized, at-most-25-row register page
    // reach this method. All lookups are tenant-scoped, projected and bounded by those IDs.
    private async Task<Dictionary<long, string>> CompletionAcademicScopeErrorsPageAsync(
        CourseEnrollment[] enrollments, Course course, CancellationToken ct)
    {
        var errors = new Dictionary<long, string>();
        if (enrollments.Length == 0) return errors;

        const string missing = "Academic enrollment is required for this course.";
        const string invalid = "Active student academic enrollment does not match this course.";
        const string dates = "Course completion is outside the student's campus, curriculum or academic dates.";
        const string subject = "Course subject is not offered in the student's curriculum.";
        var candidates = new List<CourseEnrollment>(enrollments.Length);
        foreach (var enrollment in enrollments)
        {
            if (enrollment.StudentEnrollmentId == null)
            {
                if (course.AcademicProgramId.HasValue || course.SubjectId.HasValue)
                    errors[enrollment.Id] = missing;
            }
            else candidates.Add(enrollment);
        }
        if (candidates.Count == 0) return errors;
        if (_submissionYears == null || _submissionBatches == null || _submissionCurricula == null)
        {
            foreach (var enrollment in candidates)
                errors[enrollment.Id] = "Academic completion repositories are unavailable.";
            return errors;
        }

        var tenant = _user.TenantId;
        var academicIds = candidates.Select(x => x.StudentEnrollmentId!.Value).Distinct().ToArray();
        var academics = await _academicEnrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && academicIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(ct);
        var academicById = academics.ToDictionary(x => x.Id);
        var matched = new List<(CourseEnrollment Enrollment, EduOS.Core.Entities.Academic.StudentEnrollment Academic)>();
        foreach (var enrollment in candidates)
        {
            if (!academicById.TryGetValue(enrollment.StudentEnrollmentId!.Value, out var academic) ||
                academic.StudentId != enrollment.StudentId || academic.State != EnrollmentState.Active ||
                !academic.IsCurrent ||
                (course.AcademicProgramId.HasValue && course.AcademicProgramId != academic.AcademicProgramId))
                errors[enrollment.Id] = invalid;
            else matched.Add((enrollment, academic));
        }
        if (matched.Count == 0) return errors;

        var yearIds = matched.Select(x => x.Academic.AcademicYearId).Distinct().ToArray();
        var batchIds = matched.Select(x => x.Academic.AcademicBatchId).Distinct().ToArray();
        var curriculumIds = matched.Select(x => x.Academic.AcademicCurriculumId).Distinct().ToArray();
        var years = (await _submissionYears.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && yearIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(ct)).ToDictionary(x => x.Id);
        var batches = (await _submissionBatches.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && batchIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(ct)).ToDictionary(x => x.Id);
        var curricula = (await _submissionCurricula.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && curriculumIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(ct)).ToDictionary(x => x.Id);

        var offered = new HashSet<(long BatchId, long YearId, long CurriculumId)>();
        if (course.SubjectId.HasValue)
        {
            var offerings = await (from offering in _offerings.GetQueryable().AsNoTracking()
                join item in _curriculumSubjects.GetQueryable().AsNoTracking()
                    on offering.CurriculumSubjectId equals item.Id
                where offering.TenantId == tenant && item.TenantId == tenant &&
                      !offering.IsDeleted && !item.IsDeleted && offering.IsActive && item.IsActive &&
                      batchIds.Contains(offering.AcademicBatchId) &&
                      yearIds.Contains(offering.AcademicYearId) &&
                      curriculumIds.Contains(item.AcademicCurriculumId) &&
                      item.SubjectId == course.SubjectId.Value
                select new { offering.AcademicBatchId, offering.AcademicYearId, item.AcademicCurriculumId })
                .Distinct().ToListAsync(ct);
            foreach (var offering in offerings)
                offered.Add((offering.AcademicBatchId, offering.AcademicYearId, offering.AcademicCurriculumId));
        }

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        foreach (var (enrollment, academic) in matched)
        {
            if (!years.TryGetValue(academic.AcademicYearId, out var year) ||
                !batches.TryGetValue(academic.AcademicBatchId, out var batch) ||
                !curricula.TryGetValue(academic.AcademicCurriculumId, out var curriculum) ||
                batch.CampusId != academic.CampusId ||
                batch.AcademicYearId != academic.AcademicYearId ||
                batch.AcademicProgramId != academic.AcademicProgramId ||
                curriculum.AcademicProgramId != academic.AcademicProgramId ||
                (year.CampusId.HasValue && year.CampusId != academic.CampusId) ||
                today < year.StartDate || today > year.EndDate ||
                today < academic.EnrollmentDate || (academic.EndDate.HasValue && today > academic.EndDate) ||
                today < curriculum.EffectiveFrom ||
                (curriculum.EffectiveTo.HasValue && today > curriculum.EffectiveTo) ||
                (batch.StartDate.HasValue && today < batch.StartDate) ||
                (batch.EndDate.HasValue && today > batch.EndDate))
                errors[enrollment.Id] = dates;
            else if (course.SubjectId.HasValue &&
                !offered.Contains((academic.AcademicBatchId, academic.AcademicYearId, academic.AcademicCurriculumId)))
                errors[enrollment.Id] = subject;
        }
        return errors;
    }
}
