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

    // This read-only OBE extension is captured in the SAME academic publication/correction
    // transaction, after canonical course rows have been persisted. During an additive
    // mixed-version deployment the table may not yet exist: the result still publishes
    // normally, while rubric attainment fails closed for that version (no register).
    private async Task CapturePublishedObeComponentsAsync(ResultPublication publication,
        DateTime now, CancellationToken ct)
    {
        if (_obeDb == null || _courseSnapshots == null || !publication.HasCourseGradeSnapshots) return;
        var installed = await _obeDb.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.ObePublishedComponentRegisters', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.ObePublishedComponentEvidence', N'U') IS NOT NULL
                THEN 1 ELSE 0 END AS Value
            """).SingleAsync(ct);
        if (installed == 0) return;
        var tenant = _user.TenantId;
        var courses = await _courseSnapshots.GetQueryable().AsNoTracking().CountAsync(x =>
            x.TenantId == tenant && x.ResultPublicationId == publication.Id && !x.IsDeleted, ct);
        if (courses == 0) return;
        var expected = await _obeDb.Database.SqlQueryRaw<long>("""
            SELECT COUNT_BIG(*) AS Value
            FROM dbo.StudentResultCourseSnapshots AS grade
            JOIN dbo.AssessmentComponents AS component
              ON component.TenantId = grade.TenantId
             AND component.AssessmentSubjectId = grade.AssessmentSubjectId
             AND component.IsDeleted = 0
            WHERE grade.TenantId = {0} AND grade.ResultPublicationId = {1}
              AND grade.IsDeleted = 0
            """, tenant, publication.Id).SingleAsync(ct);
        // OBE is optional reporting evidence, never a reason to block a large
        // valid academic publication. The absence of a register forbids rubric use.
        if (expected > 75000)
        {
            _logger.LogWarning("OBE component capture exceeded 75K rows for tenant {TenantId}, publication {PublicationId}",
                tenant, publication.Id);
            return;
        }
        await _obeDb.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ObePublishedComponentEvidence
              (TenantId, ResultPublicationId, StudentEnrollmentId, CurriculumSubjectId,
               AssessmentComponentId, ComponentCode, FullMarks, ObtainedMarks, IsAbsent, IsWithheld)
            SELECT grade.TenantId, grade.ResultPublicationId, grade.StudentEnrollmentId,
                grade.CurriculumSubjectId, component.Id, component.Code, component.FullMarks,
                detail.ObtainedMarks, detail.IsAbsent, detail.IsWithheld
            FROM dbo.StudentResultCourseSnapshots AS grade
            JOIN dbo.AssessmentComponents AS component
              ON component.TenantId = grade.TenantId
             AND component.AssessmentSubjectId = grade.AssessmentSubjectId
             AND component.IsDeleted = 0
            JOIN dbo.StudentAssessmentMarks AS master
              ON master.TenantId = grade.TenantId
             AND master.AssessmentSubjectId = grade.AssessmentSubjectId
             AND master.StudentSubjectRegistrationId = grade.StudentSubjectRegistrationId
             AND master.IsDeleted = 0
            JOIN dbo.StudentAssessmentComponentMarks AS detail
              ON detail.TenantId = grade.TenantId AND detail.AssessmentComponentId = component.Id
             AND detail.StudentAssessmentMarkId = master.Id AND detail.IsDeleted = 0
            WHERE grade.TenantId = {tenant} AND grade.ResultPublicationId = {publication.Id}
              AND grade.IsDeleted = 0
            """, ct);
        var actual = await _obeDb.Database.SqlQueryRaw<long>("""
            SELECT COUNT_BIG(*) AS Value FROM dbo.ObePublishedComponentEvidence
            WHERE TenantId = {0} AND ResultPublicationId = {1}
            """, tenant, publication.Id).SingleAsync(ct);
        if (actual != expected)
            throw new InvalidOperationException("Published component evidence is incomplete; academic transaction rolled back.");
        await _obeDb.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ObePublishedComponentRegisters
              (TenantId, ResultPublicationId, CourseRowCount, ComponentRowCount, CapturedAtUtc)
            VALUES ({tenant}, {publication.Id}, {courses}, {(int)actual}, {now})
            """, ct);
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

        // EF Core/SQL Server does not translate some left joins against grouped
        // aggregates. Aggregate separately on SQL Server, then compare only the
        // bounded, tenant-scoped summaries in memory. The query-filter bypass
        // deliberately sees soft-deleted historical evidence.
        for (var offset = 0; offset < flagged.Length; offset += 5)
        {
            var ids = flagged.Skip(offset).Take(5).ToArray();
            var summaries = await _summaries.GetQueryable().AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.TenantId == tenant && ids.Contains(x.ResultPublicationId))
                .Select(x => new { x.ResultPublicationId, x.StudentEnrollmentId,
                    x.IsDeleted, x.CourseCount, x.TotalMarks, x.ObtainedMarks,
                    x.CreditHours, x.GPA })
                .Take(25001).ToListAsync(ct);
            var aggregates = await _courseSnapshots.GetQueryable().AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.TenantId == tenant && ids.Contains(x.ResultPublicationId))
                .GroupBy(x => new { x.ResultPublicationId, x.StudentEnrollmentId })
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
                }).Take(25001).ToListAsync(ct);
            if (summaries.Count > 25000 || aggregates.Count > 25000)
            {
                foreach (var id in ids) result[id] = false;
                continue;
            }
            var courseByKey = aggregates.ToDictionary(x => (x.ResultPublicationId, x.StudentEnrollmentId));
            var summaryKeys = summaries.Select(x => (x.ResultPublicationId, x.StudentEnrollmentId)).ToHashSet();
            foreach (var course in aggregates)
                if (!summaryKeys.Contains((course.ResultPublicationId, course.StudentEnrollmentId)))
                    result[course.ResultPublicationId] = false;
            foreach (var summary in summaries)
            {
                if (!courseByKey.TryGetValue((summary.ResultPublicationId, summary.StudentEnrollmentId),
                        out var course) ||
                    summary.IsDeleted || !summary.CourseCount.HasValue || summary.CourseCount <= 0 ||
                    course.Count != summary.CourseCount || course.Count != course.Active || course.Bad > 0 ||
                    course.Full != summary.TotalMarks || course.Obtained != summary.ObtainedMarks ||
                    (summary.CreditHours.HasValue &&
                        (course.Credits != summary.CreditHours || !summary.GPA.HasValue ||
                         Math.Abs(course.WeightedPoints - summary.GPA.Value * summary.CreditHours.Value) >
                            summary.CreditHours.Value * 0.0001m)))
                    result[summary.ResultPublicationId] = false;
            }
        }
        return result;
    }
}
