using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Library;

public sealed class GroundedStudySourceDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string? Edition { get; set; }
    public string Chapter { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int ApprovedPassages { get; set; }
}

public sealed class GroundedStudyQuestionDto
{
    [Required] public Guid SourceReference { get; set; }
    [Required, StringLength(300, MinimumLength = 2)] public string Question { get; set; } = string.Empty;
}

public sealed class GroundedStudyAnswerDto
{
    public string Outcome { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Citation { get; set; }
    public string? StudySuggestion { get; set; }
}

public sealed class GroundedStudyExcerptDto
{
    public Guid Reference { get; set; }
    public Guid SourceReference { get; set; }
    public string SourceLocator { get; set; } = string.Empty;
    public string ExcerptText { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? ReviewReason { get; set; }
}

public sealed class CreateGroundedStudyExcerptDto
{
    [Required] public Guid SourceReference { get; set; }
    [Required, StringLength(160, MinimumLength = 2)] public string SourceLocator { get; set; } = string.Empty;
    [Required, StringLength(1200, MinimumLength = 10)] public string ExcerptText { get; set; } = string.Empty;
    [Required] public string LanguageCode { get; set; } = "bn-BD";
}

public sealed class ReviewGroundedStudyExcerptDto
{
    [Required] public string Decision { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
    [StringLength(300)] public string? Reason { get; set; }
}
