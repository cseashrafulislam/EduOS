using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Files;

public class FileAsset : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(100)] public string StorageProvider { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string StorageKey { get; set; } = string.Empty;
    [Required, MaxLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    [MaxLength(128)] public string? Sha256 { get; set; }
    public long? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(30)] public string MalwareScanStateCode { get; set; } = "Pending";
    public DateTime? MalwareScanCompletedAt { get; set; }
    public FileVisibility Visibility { get; set; } = FileVisibility.Private;
    public bool IsVerifiedSafe { get; set; }
}

public class Document : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long FileAssetId { get; set; }
    [Required, MaxLength(100)] public string DocumentTypeCode { get; set; } = string.Empty;
    public int VersionNo { get; set; } = 1;
    public long? SupersedesDocumentId { get; set; }
    // Null keeps historical institute-wide digital licences; non-null limits access to one active campus.
    public long? LibraryCampusId { get; set; }
    // Licensed education content metadata is version-specific; Book remains the
    // authoritative bibliographic catalogue and Document owns each publication.
    public long? ContentCurriculumId { get; set; }
    public long? ContentLevelId { get; set; }
    public long? ContentSubjectId { get; set; }
    [MaxLength(150)] public string? ContentChapter { get; set; }
    [MaxLength(150)] public string? ContentTopic { get; set; }
    [MaxLength(10)] public string? ContentLanguageCode { get; set; }
    [MaxLength(1000)] public string? ContentSourceUrl { get; set; }
    [MaxLength(200)] public string? ContentRightsHolder { get; set; }
    [MaxLength(32)] public string? ContentLicenceCode { get; set; }
    [MaxLength(500)] public string? ContentLicenceEvidence { get; set; }
    // Optional Bangladesh study classification; each edition remains a reviewed Document.
    // No separate QuestionBank, book store or editable syllabus-copy record is created.
    [MaxLength(24)] public string? ContentExamCode { get; set; }
    [MaxLength(40)] public string? ContentBoardCode { get; set; }
    public int? ContentSyllabusYear { get; set; }
    [MaxLength(50)] public string? ContentSyllabusVersion { get; set; }
    [MaxLength(1000)] public string? ContentSyllabusSourceUrl { get; set; }
    public int? ContentPreviousPaperYear { get; set; }
    [MaxLength(2000)] public string? ContentStudyExplanation { get; set; }
    // False for all historical publications; an explicit, independently reviewed
    // licence grant is required before the learner can retain an offline copy.
    public bool ContentOfflineAllowed { get; set; }
    [MaxLength(100)] public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    [MaxLength(250)] public string? Title { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(24)] public string VerificationStatusCode { get; set; } = "NotRequired";
    public long? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    [MaxLength(500)] public string? VerificationNote { get; set; }
}

public class DocumentTemplate : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public string TemplateContent { get; set; } = string.Empty;
    [MaxLength(50)] public string Format { get; set; } = "HTML";
    public bool IsActive { get; set; } = true;
}

public class DocumentTypeDefinition : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
    public bool RequiresVerification { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsActive { get; set; } = true;
}
