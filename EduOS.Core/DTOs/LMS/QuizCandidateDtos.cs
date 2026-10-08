namespace EduOS.Core.DTOs.LMS;
// Deliberately omits correct-answer flags and marking information.
public sealed class QuizCandidateDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public IReadOnlyList<QuizCandidateQuestionDto> Questions { get; set; } = Array.Empty<QuizCandidateQuestionDto>();
}
public sealed class QuizCandidateQuestionDto
{
    public long Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = string.Empty;
    public decimal Marks { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyList<QuizCandidateOptionDto> Options { get; set; } = Array.Empty<QuizCandidateOptionDto>();
}
public sealed class QuizCandidateOptionDto
{
    public long Id { get; set; }
    public string OptionText { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
