using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

/// <summary>
/// Review categories from immutable published results. This is a screening register,
/// NOT an automatic retake/improvement entitlement or enrollment decision.
/// </summary>
public enum ResultFollowUpStatus { Failed = 1, Withheld = 2, Both = 3 }

public sealed class ResultFollowUpQueryDto
{
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    public ResultFollowUpStatus Status { get; set; } = ResultFollowUpStatus.Both;
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 25;
}

public sealed class ResultFollowUpRowDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public decimal ObtainedMarks { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal? GPA { get; set; }
    public string? GradeLetter { get; set; }
    public bool IsWithheld { get; set; }
}

public sealed class ResultFollowUpPageDto
{
    public long PublicationId { get; set; }
    public int VersionNo { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public string AcademicBatchName { get; set; } = string.Empty;
    public ResultFollowUpStatus Status { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<ResultFollowUpRowDto> Items { get; set; } = [];
}
