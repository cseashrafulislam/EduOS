using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

// Historical publication versions remain reviewable by authorized exam controllers after withdrawal.
public sealed class AssessmentPublicationHistoryQueryDto : AssessmentScopeDto
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed class AssessmentPublicationHistoryRowDto
{
    public long Id { get; set; }
    public int VersionNo { get; set; }
    public ResultPublicationState State { get; set; }
    public DateTime? PublishedAt { get; set; }
    public long? PublishedByUserId { get; set; }
    public bool VisibleToStudent { get; set; }
    public bool VisibleToGuardian { get; set; }
    public string? PublishNote { get; set; }
    public int SnapshotCount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssessmentPublicationHistoryPageDto
{
    public long AssessmentId { get; set; }
    public long AcademicBatchId { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<AssessmentPublicationHistoryRowDto> Items { get; set; } =
        Array.Empty<AssessmentPublicationHistoryRowDto>();
}

public sealed class PublishedTabulationQueryDto
{
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 50;
}

public sealed class PublishedTabulationRowDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public decimal TotalMarks { get; set; }
    public decimal ObtainedMarks { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? GPA { get; set; }
    public decimal? CGPA { get; set; }
    public string? GradeLetter { get; set; }
    public int? MeritPosition { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
}

public sealed class PublishedTabulationPageDto
{
    public long PublicationId { get; set; }
    public int VersionNo { get; set; }
    public ResultPublicationState State { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public string AcademicBatchName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<PublishedTabulationRowDto> Items { get; set; } =
        Array.Empty<PublishedTabulationRowDto>();
}
