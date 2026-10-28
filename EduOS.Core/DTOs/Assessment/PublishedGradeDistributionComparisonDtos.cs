using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

/// <summary>Compare two finalized snapshots of the same assessment and cohort, never live marks.</summary>
public sealed class PublishedGradeDistributionComparisonQueryDto
{
    [Range(1, long.MaxValue)] public long BaselinePublicationId { get; set; }
    [Range(1, long.MaxValue)] public long TargetPublicationId { get; set; }
}

public sealed class PublishedGradeDistributionComparisonRowDto
{
    public string GradeLetter { get; set; } = "";
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
    public int BaselineCount { get; set; }
    public int TargetCount { get; set; }
    public int CountChange { get; set; }
    public decimal BaselineTotalObtainedMarks { get; set; }
    public decimal TargetTotalObtainedMarks { get; set; }
    public decimal ObtainedMarksChange { get; set; }
    public decimal? BaselineAveragePercentage { get; set; }
    public decimal? TargetAveragePercentage { get; set; }
    public decimal? AveragePercentageChange { get; set; }
    public decimal? BaselineAverageGpa { get; set; }
    public decimal? TargetAverageGpa { get; set; }
}

public sealed class PublishedGradeDistributionComparisonDto
{
    public long BaselinePublicationId { get; set; }
    public long TargetPublicationId { get; set; }
    public int BaselineVersionNo { get; set; }
    public int TargetVersionNo { get; set; }
    public string AssessmentName { get; set; } = "";
    public string AcademicBatchName { get; set; } = "";
    public int BaselineStudentCount { get; set; }
    public int TargetStudentCount { get; set; }
    public int StudentCountChange { get; set; }
    public int BaselinePassedCount { get; set; }
    public int TargetPassedCount { get; set; }
    public int BaselineFailedCount { get; set; }
    public int TargetFailedCount { get; set; }
    public int BaselineWithheldCount { get; set; }
    public int TargetWithheldCount { get; set; }
    public IReadOnlyList<PublishedGradeDistributionComparisonRowDto> Grades { get; set; } =
        Array.Empty<PublishedGradeDistributionComparisonRowDto>();
}
