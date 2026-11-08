using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class CreateOmrTemplateRequestDto
{
    [Range(1, long.MaxValue)] public long AssessmentComponentId { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string AnswerKey { get; set; } = string.Empty;
    [Range(4, 5)] public int OptionsPerQuestion { get; set; } = 4;
    [Range(typeof(decimal), "0.01", "1000")] public decimal CorrectAnswerMarks { get; set; } = 1m;
    [Range(typeof(decimal), "0", "1000")] public decimal IncorrectAnswerMarks { get; set; }
}

public sealed class OmrTemplateDto
{
    public Guid Reference { get; set; }
    public long AssessmentComponentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public int OptionsPerQuestion { get; set; }
    public decimal CorrectAnswerMarks { get; set; }
    public decimal IncorrectAnswerMarks { get; set; }
    public DateTime SealedAtUtc { get; set; }
}

public sealed class CaptureOmrSheetRequestDto
{
    public Guid RequestId { get; set; }
    public Guid TemplateReference { get; set; }
    [Range(1, long.MaxValue)] public long StudentSubjectRegistrationId { get; set; }
    public Guid FileReference { get; set; }
}

public sealed class ReviewOmrSheetRequestDto
{
    public Guid RequestId { get; set; }
    [Required, MaxLength(200)] public string ConfirmedAnswers { get; set; } = string.Empty;
    [MaxLength(500)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool IdentityConfirmed { get; set; }
}

public sealed class PostOmrSheetRequestDto
{
    public Guid RequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class OmrSheetDto
{
    public Guid Reference { get; set; }
    public Guid TemplateReference { get; set; }
    public Guid FileReference { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    public string SuggestedAnswers { get; set; } = string.Empty;
    public string SuggestionStates { get; set; } = string.Empty;
    public string ConfirmedAnswers { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int UncertainCount { get; set; }
    public decimal? ProposedMarks { get; set; }
    public long CapturedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public long? PostedByUserId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class OmrSheetPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<OmrSheetDto> Items { get; set; } = [];
}

public sealed class OmrExportRequestDto
{
    public Guid TemplateReference { get; set; }
    [Required] public string Format { get; set; } = "xlsx";
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
}

public sealed class OmrExportFileDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}
