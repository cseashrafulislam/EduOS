using System.ComponentModel.DataAnnotations;
using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentMarkReviewRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string AssessmentRowVersion { get; set; } = "";
    public AssessmentMarkReviewDecision Decision { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public sealed class AssessmentMarkReviewEventDto
{
    public long Id { get; set; }
    public AssessmentMarkReviewDecision Decision { get; set; }
    public DateTime OccurredAt { get; set; }
    public long ActorUserId { get; set; }
    public string? Reason { get; set; }
}

public sealed class AssessmentMarkReviewDto
{
    public long AssessmentSubjectId { get; set; }
    public string AssessmentRowVersion { get; set; } = "";
    public AssessmentMarkReviewDecision? CurrentDecision { get; set; }
    public bool IsFinalized { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanReview { get; set; }
    public int HistoryTotal { get; set; }
    public IReadOnlyList<AssessmentMarkReviewEventDto> RecentHistory { get; set; } = [];
}

// Immutable review decisions are paged by event ID so concurrent submissions cannot
// shift older pages or silently omit audit history.
public sealed class AssessmentMarkReviewHistoryQueryDto
{
    [Range(1, long.MaxValue)] public long? BeforeId { get; set; }
    [Range(1, 50)] public int PageSize { get; set; } = 25;
    public AssessmentMarkReviewDecision? Decision { get; set; }
}

public sealed class AssessmentMarkReviewHistoryPageDto
{
    public IReadOnlyList<AssessmentMarkReviewEventDto> Items { get; set; } = [];
    public bool HasMore { get; set; }
    public long? NextBeforeId { get; set; }
}
