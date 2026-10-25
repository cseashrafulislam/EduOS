using EduOS.Core.Enums.Domain;
namespace EduOS.Core.DTOs.LMS;

/// <summary>Read-only learner-owned attempt evidence, without answer keys.</summary>
public sealed class QuizHistoryRowDto
{
    public long AttemptId { get; set; }
    public Guid QuizReference { get; set; }
    public string QuizTitle { get; set; } = string.Empty;
    public int AttemptNo { get; set; }
    public QuizAttemptState State { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public decimal? ObtainedMarks { get; set; }
    public decimal PassMarks { get; set; }
    public bool? Passed { get; set; }
    public bool IsArchived { get; set; }
}
