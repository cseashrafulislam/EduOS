using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

/// <summary>Aggregate only; no student identities or live-grade recalculation.</summary>
public sealed class PublishedGradeDistributionQueryDto
{
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
}

public sealed class PublishedGradeDistributionRowDto
{
    public string GradeLetter { get; set; } = "";
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
    public int StudentCount { get; set; }
    public decimal TotalObtainedMarks { get; set; }
    public decimal? AveragePercentage { get; set; }
    public decimal? AverageGpa { get; set; }
}

public sealed class PublishedGradeDistributionDto
{
    public long PublicationId { get; set; }
    public int VersionNo { get; set; }
    public string AssessmentName { get; set; } = "";
    public string AcademicBatchName { get; set; } = "";
    public int StudentCount { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int WithheldCount { get; set; }
    public IReadOnlyList<PublishedGradeDistributionRowDto> Grades { get; set; } =
        Array.Empty<PublishedGradeDistributionRowDto>();
}
