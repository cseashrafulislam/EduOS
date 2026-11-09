using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Library;

/// <summary>Teacher-reviewed, version-specific short passage. Document owns book, edition,
/// chapter, licence and campus; none of those editable facts are copied here.</summary>
public sealed class StudyEvidenceExcerpt : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long DocumentId { get; set; }
    [Required, MaxLength(160)] public string SourceLocator { get; set; } = string.Empty;
    [Required, MaxLength(1200)] public string ExcerptText { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string LanguageCode { get; set; } = "bn-BD";
    [Required, MaxLength(16)] public string StateCode { get; set; } = "Pending";
    public long SubmittedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(300)] public string? ReviewReason { get; set; }
}
