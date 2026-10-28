using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Assessment;

public sealed class PublishedVersionComparisonQueryDto
{
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    [Range(1, long.MaxValue)] public long? BaselinePublicationId { get; set; }
    [MaxLength(100)] public string? Search { get; set; }
    public bool ChangesOnly { get; set; } = true;
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 25;
}
public sealed class PublishedVersionComparisonRowDto
{
    public long StudentEnrollmentId { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public decimal? BeforeObtainedMarks { get; set; }
    public decimal? AfterObtainedMarks { get; set; }
    public decimal? BeforeTotalMarks { get; set; }
    public decimal? AfterTotalMarks { get; set; }
    public decimal? BeforeGpa { get; set; }
    public decimal? AfterGpa { get; set; }
    public decimal? BeforeCgpa { get; set; }
    public decimal? AfterCgpa { get; set; }
    public decimal? BeforePercentage { get; set; }
    public decimal? AfterPercentage { get; set; }
    public string? BeforeGrade { get; set; }
    public string? AfterGrade { get; set; }
    public int? BeforeMerit { get; set; }
    public int? AfterMerit { get; set; }
    public bool? BeforePassed { get; set; }
    public bool? AfterPassed { get; set; }
    public bool? BeforeWithheld { get; set; }
    public bool? AfterWithheld { get; set; }
}
public sealed class PublishedVersionComparisonPageDto
{
    public long BaselinePublicationId { get; set; }
    public int BaselineVersionNo { get; set; }
    public long PublicationId { get; set; }
    public int VersionNo { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<PublishedVersionComparisonRowDto> Items { get; set; } =
        Array.Empty<PublishedVersionComparisonRowDto>();
}
