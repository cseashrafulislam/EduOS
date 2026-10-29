using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    private const int MaxSynchronousCourseGradeSnapshots = 25000;

    private static bool CanCaptureCourseSnapshots(AssessmentResultSheetDto sheet) =>
        sheet.Results.Count > 0 &&
        sheet.Results.Sum(row => (long)row.Courses.Count) <= MaxSynchronousCourseGradeSnapshots &&
        sheet.Results.All(row => row.Courses.Count > 0 && row.Courses.All(course =>
            course.AssessmentSubjectId > 0 && course.StudentSubjectRegistrationId > 0 &&
            course.CurriculumSubjectId > 0 && course.FullMarks > 0 &&
            course.ObtainedMarks >= 0 && course.ObtainedMarks <= course.FullMarks &&
            course.CreditHours >= 0 && course.GradePoint >= 0 &&
            !string.IsNullOrWhiteSpace(course.GradeLetter)));

    private async Task CaptureCourseSnapshotsAsync(ResultPublication publication,
        AssessmentResultSheetDto sheet, IReadOnlyDictionary<string, long> enrollmentByRoll,
        DateTime now, CancellationToken token)
    {
        if (_courseSnapshots == null || !publication.HasCourseGradeSnapshots) return;
        foreach (var row in sheet.Results)
        {
            var enrollmentId = enrollmentByRoll[row.RollNo];
            foreach (var course in row.Courses)
                await _courseSnapshots.AddAsync(new StudentResultCourseSnapshot
                {
                    TenantId = _user.TenantId, ResultPublicationId = publication.Id,
                    StudentEnrollmentId = enrollmentId,
                    AssessmentSubjectId = course.AssessmentSubjectId,
                    CurriculumSubjectId = course.CurriculumSubjectId,
                    StudentSubjectRegistrationId = course.StudentSubjectRegistrationId,
                    FullMarks = course.FullMarks, ObtainedMarks = course.ObtainedMarks,
                    CreditHours = course.CreditHours, GradePoint = course.GradePoint,
                    GradeLetter = course.GradeLetter, IsAbsent = course.IsAbsent,
                    IsWithheld = course.IsWithheld, IsPassed = course.IsPassed,
                    CreatedAt = now, CreatedBy = _user.UserId
                });
        }
    }

    // Check the complete published version, not merely the requested page. A deleted,
    // missing, duplicate or cross-publication course must fail the same integrity
    // gate as a damaged summary. Old publications without course evidence are
    // intentionally unaffected by this additive migration.
    private async Task<Dictionary<long, bool>> ReconcileCourseSnapshotPublicationsAsync(
        long[] publicationIds, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var flagged = await _publications.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && publicationIds.Contains(x.Id) &&
                x.HasCourseGradeSnapshots && !x.IsDeleted)
            .Select(x => x.Id).ToArrayAsync(ct);
        var result = flagged.ToDictionary(x => x, _ => true);
        if (flagged.Length == 0) return result;
        if (_courseSnapshots == null)
            return flagged.ToDictionary(x => x, _ => false);

        // Query-filter bypass is narrow and tenant-scoped so that soft-deleted
        // course evidence is counted as a reconciliation error rather than hidden.
        var physical = _courseSnapshots.GetQueryable().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.TenantId == tenant && flagged.Contains(x.ResultPublicationId));
        var aggregates = physical.GroupBy(x => new { x.ResultPublicationId, x.StudentEnrollmentId })
            .Select(g => new
            {
                g.Key.ResultPublicationId, g.Key.StudentEnrollmentId,
                Count = g.Count(), Active = g.Count(x => !x.IsDeleted),
                Bad = g.Count(x => x.IsDeleted || x.AssessmentSubjectId <= 0 ||
                    x.StudentSubjectRegistrationId <= 0 || x.CurriculumSubjectId <= 0 ||
                    x.FullMarks <= 0m || x.ObtainedMarks < 0m || x.ObtainedMarks > x.FullMarks ||
                    x.CreditHours < 0m || x.GradePoint < 0m || x.GradeLetter == ""),
                Full = g.Sum(x => x.FullMarks), Obtained = g.Sum(x => x.ObtainedMarks),
                Credits = g.Sum(x => x.CreditHours),
                WeightedPoints = g.Sum(x => x.CreditHours * x.GradePoint)
            });
        var invalid = await (
            from summary in _summaries.GetQueryable().AsNoTracking()
            join course in aggregates
                on new { summary.ResultPublicationId, summary.StudentEnrollmentId }
                equals new { course.ResultPublicationId, course.StudentEnrollmentId }
                into collection
            from course in collection.DefaultIfEmpty()
            where summary.TenantId == tenant && flagged.Contains(summary.ResultPublicationId) &&
                (course == null || !summary.CourseCount.HasValue || summary.CourseCount <= 0 ||
                 course.Count != summary.CourseCount || course.Count != course.Active || course.Bad > 0 ||
                 course.Full != summary.TotalMarks || course.Obtained != summary.ObtainedMarks ||
                 (summary.CreditHours.HasValue &&
                     (course.Credits != summary.CreditHours || summary.GPA == null ||
                      Math.Abs(course.WeightedPoints - summary.GPA.Value * summary.CreditHours.Value) >
                         summary.CreditHours.Value * 0.0001m)))
            select summary.ResultPublicationId).Distinct().ToArrayAsync(ct);

        var orphans = await physical.Where(course =>
            !_summaries.GetQueryable().AsNoTracking().IgnoreQueryFilters().Any(summary =>
                summary.TenantId == tenant && summary.ResultPublicationId == course.ResultPublicationId &&
                summary.StudentEnrollmentId == course.StudentEnrollmentId && !summary.IsDeleted))
            .Select(x => x.ResultPublicationId).Distinct().ToArrayAsync(ct);
        foreach (var id in invalid.Concat(orphans)) result[id] = false;
        return result;
    }
}
