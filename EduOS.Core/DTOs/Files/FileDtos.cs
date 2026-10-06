namespace EduOS.Core.DTOs.Files;

public class FileAssetDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string StorageProvider { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public FileVisibility Visibility { get; set; }
    public bool IsVerifiedSafe { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateFileAssetMetadataRequestDto
{
    [Required, MaxLength(100)] public string StorageProvider { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string StorageKey { get; set; } = string.Empty;
    [Required, MaxLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string ContentType { get; set; } = string.Empty;
    [Range(1, long.MaxValue)] public long SizeBytes { get; set; }
    [MaxLength(128)] public string? Sha256 { get; set; }
    public FileVisibility Visibility { get; set; } = FileVisibility.Private;
}

public class DocumentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid FileReference { get; set; }
    public string DocumentTypeCode { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public string? Title { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveDocumentRequestDto
{
    public Guid FileReference { get; set; }
    [Required, MaxLength(100)] public string DocumentTypeCode { get; set; } = string.Empty;
    [MaxLength(100)] public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    [MaxLength(250)] public string? Title { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class DocumentTemplateDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TemplateContent { get; set; } = string.Empty;
    public string Format { get; set; } = "HTML";
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveDocumentTemplateRequestDto
{
    [Required, MaxLength(100)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public string TemplateContent { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Format { get; set; } = "HTML";
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
