using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<PublishedGradeDistributionComparisonDto>> ComparePublishedGradeDistributionsAsync(
        PublishedGradeDistributionComparisonQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<PublishedGradeDistributionComparisonDto>("Historical result report permission required.", 403);
        if (request == null || request.BaselinePublicationId <= 0 || request.TargetPublicationId <= 0 ||
            request.BaselinePublicationId == request.TargetPublicationId)
            return Error<PublishedGradeDistributionComparisonDto>("Two distinct publication versions are required.");

        var tenant = _user.TenantId;
        var versions = await _publications.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                (x.Id == request.BaselinePublicationId || x.Id == request.TargetPublicationId))
            .Select(x => new { x.Id, x.AssessmentId, x.AcademicBatchId, x.VersionNo, x.State })
            .Take(3).ToListAsync(ct);
        if (versions.Count != 2)
            return Error<PublishedGradeDistributionComparisonDto>("Publication version not found.", 404);
        var baseline = versions.Single(x => x.Id == request.BaselinePublicationId);
        var target = versions.Single(x => x.Id == request.TargetPublicationId);
        if (baseline.AssessmentId != target.AssessmentId ||
            baseline.AcademicBatchId != target.AcademicBatchId ||
            baseline.VersionNo >= target.VersionNo ||
            baseline.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn) ||
            target.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn))
            return Error<PublishedGradeDistributionComparisonDto>(
                "Select two ordered finalized versions from the same assessment and batch.", 409);

        // Both report calls independently enforce campus access, tenant ownership, EXAM role,
        // and full immutable snapshot reconciliation. Never compare a partial snapshot.
        var before = await GetPublishedGradeDistributionAsync(new()
        { PublicationId = baseline.Id }, ct);
        if (!before.Success || before.Data == null)
            return Error<PublishedGradeDistributionComparisonDto>(before.Message, before.StatusCode);
        var after = await GetPublishedGradeDistributionAsync(new()
        { PublicationId = target.Id }, ct);
        if (!after.Success || after.Data == null)
            return Error<PublishedGradeDistributionComparisonDto>(after.Message, after.StatusCode);

        return ApiResponse<PublishedGradeDistributionComparisonDto>.SuccessResponse(
            PublishedGradeDistributionComparisonCalculator.Compare(before.Data, after.Data));
    }
}

/// <summary>Pure, bounded comparison of independently reconciled publication aggregates.</summary>
public static class PublishedGradeDistributionComparisonCalculator
{
    public static PublishedGradeDistributionComparisonDto Compare(
        PublishedGradeDistributionDto baseline, PublishedGradeDistributionDto target)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(target);
        var before = baseline.Grades.ToDictionary(x =>
            (x.GradeLetter, x.IsPassed, x.IsWithheld));
        var after = target.Grades.ToDictionary(x =>
            (x.GradeLetter, x.IsPassed, x.IsWithheld));
        var keys = before.Keys.Union(after.Keys)
            .OrderBy(x => x.GradeLetter, StringComparer.Ordinal)
            .ThenBy(x => x.IsWithheld).ThenBy(x => x.IsPassed);
        return new PublishedGradeDistributionComparisonDto
        {
            BaselinePublicationId = baseline.PublicationId,
            TargetPublicationId = target.PublicationId,
            BaselineVersionNo = baseline.VersionNo,
            TargetVersionNo = target.VersionNo,
            AssessmentName = target.AssessmentName,
            AcademicBatchName = target.AcademicBatchName,
            BaselineStudentCount = baseline.StudentCount,
            TargetStudentCount = target.StudentCount,
            StudentCountChange = target.StudentCount - baseline.StudentCount,
            BaselinePassedCount = baseline.PassedCount,
            TargetPassedCount = target.PassedCount,
            BaselineFailedCount = baseline.FailedCount,
            TargetFailedCount = target.FailedCount,
            BaselineWithheldCount = baseline.WithheldCount,
            TargetWithheldCount = target.WithheldCount,
            Grades = keys.Select(key =>
            {
                before.TryGetValue(key, out var left);
                after.TryGetValue(key, out var right);
                return new PublishedGradeDistributionComparisonRowDto
                {
                    GradeLetter = key.GradeLetter,
                    IsPassed = key.IsPassed, IsWithheld = key.IsWithheld,
                    BaselineCount = left?.StudentCount ?? 0,
                    TargetCount = right?.StudentCount ?? 0,
                    CountChange = (right?.StudentCount ?? 0) - (left?.StudentCount ?? 0),
                    BaselineTotalObtainedMarks = left?.TotalObtainedMarks ?? 0m,
                    TargetTotalObtainedMarks = right?.TotalObtainedMarks ?? 0m,
                    ObtainedMarksChange = (right?.TotalObtainedMarks ?? 0m) -
                        (left?.TotalObtainedMarks ?? 0m),
                    BaselineAveragePercentage = left?.AveragePercentage,
                    TargetAveragePercentage = right?.AveragePercentage,
                    AveragePercentageChange = left?.AveragePercentage is decimal oldPercentage &&
                        right?.AveragePercentage is decimal newPercentage
                        ? Math.Round(newPercentage - oldPercentage, 4) : null,
                    BaselineAverageGpa = left?.AverageGpa,
                    TargetAverageGpa = right?.AverageGpa
                };
            }).ToArray()
        };
    }
}
