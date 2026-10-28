using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    /// <summary>
    /// SQL-aggregated, version-pinned historical grade distribution. A corrupt or deleted
    /// snapshot must never produce a plausible-looking partial report.
    /// </summary>
    public async Task<ApiResponse<PublishedGradeDistributionDto>> GetPublishedGradeDistributionAsync(
        PublishedGradeDistributionQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<PublishedGradeDistributionDto>("Result report permission required.", 403);
        if (request == null || request.PublicationId <= 0)
            return Error<PublishedGradeDistributionDto>("Valid publication version required.");

        var tenant = _user.TenantId;
        var publication = await _publications.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == request.PublicationId && !x.IsDeleted, ct);
        if (publication == null)
            return Error<PublishedGradeDistributionDto>("Publication not found.", 404);
        if (publication.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn))
            return Error<PublishedGradeDistributionDto>("Only finalized result versions can be reported.", 409);

        // Archived academic batches remain valid historical scopes. Do not require IsActive.
        var scope = await (from exam in _assessments.GetQueryable().AsNoTracking()
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { exam.TenantId, CampusId = exam.CampusId, YearId = exam.AcademicYearId }
                equals new { batch.TenantId, CampusId = batch.CampusId, YearId = batch.AcademicYearId }
            where exam.TenantId == tenant && exam.Id == publication.AssessmentId &&
                batch.Id == publication.AcademicBatchId && !exam.IsDeleted && !batch.IsDeleted
            select new { exam.CampusId, exam.AcademicYearId,
                AssessmentName = exam.Name, BatchName = batch.Name }).FirstOrDefaultAsync(ct);
        if (scope == null)
            return Error<PublishedGradeDistributionDto>("Publication scope is inconsistent.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<PublishedGradeDistributionDto>("Campus access denied.", 403);

        // Reconcile ALL rows (including soft-deleted evidence) before aggregation/export.
        var evidence = await GetPublicationEvidenceAsync([publication.Id],
            scope.CampusId, scope.AcademicYearId, ct);
        if (!evidence.TryGetValue(publication.Id, out var proof) || !proof.IsReconciled)
            return Error<PublishedGradeDistributionDto>(
                "Publication snapshot requires reconciliation before grade reporting.", 409);

        // Group in SQL; never materialize a learner roster. Bound distinct grade/status groups.
        var grouped = await _summaries.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ResultPublicationId == publication.Id &&
                x.AssessmentId == publication.AssessmentId &&
                x.PublicationVersionNo == publication.VersionNo && !x.IsDeleted)
            .GroupBy(x => new { x.GradeLetter, x.IsPassed, x.IsWithheld })
            .Select(g => new
            {
                g.Key.GradeLetter, g.Key.IsPassed, g.Key.IsWithheld,
                Count = g.Count(),
                Obtained = g.Sum(x => x.ObtainedMarks),
                Percentage = g.Average(x => x.Percentage),
                Gpa = g.Average(x => x.GPA)
            })
            .OrderBy(x => x.GradeLetter).ThenBy(x => x.IsWithheld).ThenBy(x => x.IsPassed)
            .Take(101).ToListAsync(ct);
        if (grouped.Count > 100)
            return Error<PublishedGradeDistributionDto>("Grade categories exceed the report limit.", 409);
        if (grouped.Sum(x => x.Count) != proof.ActiveCount)
            return Error<PublishedGradeDistributionDto>("Snapshot changed during report generation; retry.", 409);

        _logger.LogInformation(
            "Historical grade distribution tenant {TenantId} actor {ActorId} publication {PublicationId} groups {GroupCount}",
            tenant, _user.UserId, publication.Id, grouped.Count);
        return ApiResponse<PublishedGradeDistributionDto>.SuccessResponse(new()
        {
            PublicationId = publication.Id, VersionNo = publication.VersionNo,
            AssessmentName = scope.AssessmentName, AcademicBatchName = scope.BatchName,
            StudentCount = proof.ActiveCount,
            PassedCount = grouped.Where(x => x.IsPassed && !x.IsWithheld).Sum(x => x.Count),
            FailedCount = grouped.Where(x => !x.IsPassed && !x.IsWithheld).Sum(x => x.Count),
            WithheldCount = grouped.Where(x => x.IsWithheld).Sum(x => x.Count),
            Grades = grouped.Select(x => new PublishedGradeDistributionRowDto
            {
                GradeLetter = x.GradeLetter ?? "Unassigned",
                IsPassed = x.IsPassed, IsWithheld = x.IsWithheld,
                StudentCount = x.Count, TotalObtainedMarks = x.Obtained,
                AveragePercentage = x.Percentage.HasValue ? Math.Round(x.Percentage.Value, 4) : null,
                AverageGpa = x.Gpa.HasValue ? Math.Round(x.Gpa.Value, 4) : null
            }).ToArray()
        });
    }
}
