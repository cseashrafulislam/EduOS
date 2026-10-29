using System.ComponentModel.DataAnnotations;
using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.DTOs.Assessment;

public sealed class RequestResultRescrutinyDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    public Guid StudentReference { get; set; }
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}

public sealed class DecideResultRescrutinyDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RequestRowVersion { get; set; } = string.Empty;
    public ResultRescrutinyAction Decision { get; set; }
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}

public sealed class ResultRescrutinyQueryDto
{
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    [Range(1, long.MaxValue)] public long? BeforeId { get; set; }
    public ResultRescrutinyAction? Status { get; set; }
    [Range(1, 50)] public int PageSize { get; set; } = 25;
}

public sealed class ResultRescrutinyItemDto
{
    public long RequestEventId { get; set; }
    public Guid CaseReference { get; set; }
    public long PublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public ResultRescrutinyAction Status { get; set; }
    public long RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public string RequestReason { get; set; } = string.Empty;
    public long? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public string RequestRowVersion { get; set; } = string.Empty;
}

public sealed class ResultRescrutinyPageDto
{
    public IReadOnlyList<ResultRescrutinyItemDto> Items { get; set; } = [];
    public bool HasMore { get; set; }
    public long? NextBeforeId { get; set; }
}

/// <summary>Publication-wide, tenant-scoped operational counts; never exposes learner PII.</summary>
public sealed class ResultRescrutinySummaryDto
{
    public long PublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public long Total { get; set; }
    public long Pending { get; set; }
    public long AcceptedForCorrection { get; set; }
    public long Rejected { get; set; }
    public DateTime? OldestPendingAt { get; set; }
}

/// <summary>Bounded case register export; Truncated warns about further pages.</summary>
public sealed class ResultRescrutinyExportDto
{
    public IReadOnlyList<ResultRescrutinyItemDto> Items { get; set; } = [];
    public bool Truncated { get; set; }
    public int Limit { get; set; } = 500;
}

public sealed class ResultRescrutinyHistoryEventDto
{
    public ResultRescrutinyAction Action { get; set; }
    public long ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ResultRescrutinyHistoryDto
{
    public Guid CaseReference { get; set; }
    public long PublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public Guid StudentReference { get; set; }
    public IReadOnlyList<ResultRescrutinyHistoryEventDto> Events { get; set; } = [];
}

public sealed class ResultRescrutinyReviewDto
{
    public Guid CaseReference { get; set; }
    public long PublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public string RequestReason { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public long RequestedByUserId { get; set; }
    public string RequestRowVersion { get; set; } = string.Empty;
    public ResultRescrutinyAction Status { get; set; }
    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public decimal ObtainedMarks { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? GPA { get; set; }
    public decimal? CGPA { get; set; }
    public string? GradeLetter { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
}

/// <summary>Live locked-mark evidence for an authorized re-scrutiny reviewer.
/// Published totals are a separate immutable snapshot, never inferred from these rows.</summary>
public sealed class ResultRescrutinyMarkEvidenceDto
{
    public Guid CaseReference { get; set; }
    public long PublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public decimal PublishedObtainedMarks { get; set; }
    public decimal PublishedTotalMarks { get; set; }
    /// <summary>UTC time of this live evidence read; not the historical publication time.</summary>
    public DateTime RetrievedAtUtc { get; set; }
    public IReadOnlyList<ResultRescrutinySubjectEvidenceDto> Subjects { get; set; } = [];
}
public sealed class ResultRescrutinySubjectEvidenceDto
{
    public long AssessmentSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal? CurrentObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    public string? CurrentGradeLetter { get; set; }
    public decimal? CurrentGradePoint { get; set; }
    public IReadOnlyList<ResultRescrutinyComponentEvidenceDto> Components { get; set; } = [];
}
public sealed class ResultRescrutinyComponentEvidenceDto
{
    public string Name { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal? CurrentObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
}
