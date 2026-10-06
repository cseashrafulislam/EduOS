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
    [MaxLength(100)] public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    [MaxLength(250)] public string? Title { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
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
