using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Assessment;

/// <summary>An immutable, tenant-owned answer key for one assessment component.</summary>
public sealed class OmrTemplate : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long AssessmentComponentId { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string AnswerKey { get; set; } = string.Empty;
    public int OptionsPerQuestion { get; set; } = 4;
    [Column(TypeName = "decimal(18,2)")] public decimal CorrectAnswerMarks { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal IncorrectAnswerMarks { get; set; }
    public DateTime SealedAtUtc { get; set; } = DateTime.UtcNow;
    public long SealedByUserId { get; set; }
}

/// <summary>A single answer-sheet attempt with immutable source lineage.</summary>
public sealed class OmrSheet : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long OmrTemplateId { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    public long FileAssetId { get; set; }
    [Required, MaxLength(200)] public string SuggestedAnswers { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string SuggestionStates { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string ConfirmedAnswers { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string State { get; set; } = "Captured";
    public int UncertainCount { get; set; }
    public long CapturedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? PostedByUserId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
}

/// <summary>Append-only review and posting decisions, including corrections and reasons.</summary>
public sealed class OmrSheetEvent : BaseTenantEntity
{
    public long OmrSheetId { get; set; }
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(24)] public string Action { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string BeforeAnswers { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string AfterAnswers { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    public long ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
