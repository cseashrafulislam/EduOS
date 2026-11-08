using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Library;

public sealed class LibraryDigitalResourceDto
{
    public Guid Reference { get; set; }
    public Guid BookReference { get; set; }
    public long? CampusId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string? BookAuthor { get; set; }
    public string? BookPublisher { get; set; }
    public string? BookEdition { get; set; }
    public string? BookIsbn { get; set; }
    public string? BookCategory { get; set; }
    public long? CurriculumId { get; set; }
    public long? LevelId { get; set; }
    public long? SubjectId { get; set; }
    public string? Chapter { get; set; }
    public string? Topic { get; set; }
    public string? LanguageCode { get; set; }
    public string? SourceUrl { get; set; }
    public string? RightsHolder { get; set; }
    public string? LicenceCode { get; set; }
    public string? LicenceEvidence { get; set; }
    public string ReviewStatus { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsActive { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public int VersionNo { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class LibraryDigitalUploadRequestDto
{
    [Required] public Guid RequestId { get; set; }
    [Required] public Guid BookReference { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    public DateOnly? ExpiresOn { get; set; }
    public long? CampusId { get; set; }
    public LibraryDigitalRightsRequestDto Rights { get; set; } = new();
}

public sealed class LibraryDigitalRightsRequestDto
{
    public long? CurriculumId { get; set; }
    public long? LevelId { get; set; }
    public long? SubjectId { get; set; }
    [MaxLength(150)] public string? Chapter { get; set; }
    [MaxLength(150)] public string? Topic { get; set; }
    [Required, MaxLength(10)] public string LanguageCode { get; set; } = "bn-BD";
    [MaxLength(1000)] public string? SourceUrl { get; set; }
    [Required, MaxLength(200)] public string RightsHolder { get; set; } = string.Empty;
    [Required, MaxLength(32)] public string LicenceCode { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string LicenceEvidence { get; set; } = string.Empty;
}

public sealed class LibraryDigitalCurriculumOptionDto
{
    public long CurriculumId { get; set; }
    public long LevelId { get; set; }
    public long SubjectId { get; set; }
    public string CurriculumName { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
}

public sealed class LibraryDigitalReplaceRequestDto
{
    [Required] public Guid RequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    public DateOnly? ExpiresOn { get; set; }
    public LibraryDigitalRightsRequestDto Rights { get; set; } = new();
}

public sealed class LibraryDigitalMetadataRequestDto
{
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    public DateOnly? ExpiresOn { get; set; }
    public long? CampusId { get; set; }
    public bool ConfirmTenantWideAccess { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LibraryDigitalRevokeRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LibraryDigitalReviewRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Note { get; set; } = string.Empty;
}

public sealed class LibraryDigitalVersionDto
{
    public Guid Reference { get; set; }
    public int VersionNo { get; set; }
    public string Title { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public string State { get; set; } = string.Empty;
}

public sealed class LibraryDigitalAuditDto
{
    public Guid ResourceReference { get; set; }
    public string CurrentTitle { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int VersionNo { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long? ActorUserId { get; set; }
}
