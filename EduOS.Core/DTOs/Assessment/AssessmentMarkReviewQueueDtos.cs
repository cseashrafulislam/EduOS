using System.ComponentModel.DataAnnotations;
using EduOS.Core.Entities.Assessment;

namespace EduOS.Core.DTOs.Assessment;

/// <summary>Reviewer-only, SQL-paged queue; no student or private mark details are returned.</summary>
public sealed class AssessmentMarkReviewQueueQueryDto
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 25;
    [MaxLength(100)] public string? Search { get; set; }
    [MaxLength(20)] public string? Status { get; set; } = "Pending";
}

public sealed class AssessmentMarkReviewQueueItemDto
{
    public long AssessmentSubjectId { get; set; }
    public string AssessmentName { get; set; } = "";
    public string AcademicBatchName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public AssessmentMarkReviewDecision? Decision { get; set; }
    public DateTime? LastDecisionAt { get; set; }
    public bool IsCurrent { get; set; }
    public bool CanReview { get; set; }
}
