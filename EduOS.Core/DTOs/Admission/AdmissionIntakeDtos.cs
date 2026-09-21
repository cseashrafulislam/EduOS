using EduOS.Core.Enums;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Admission;

public class AdmissionIntakeFormInputDto
{
    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_-]*$")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(1, long.MaxValue)] public long AcademicYearId { get; set; }
    [Range(1, long.MaxValue)] public long? AcademicTermId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicUnitId { get; set; }
    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal ApplicationFee { get; set; }

    [Required, RegularExpression("^[A-Za-z]{3}$")]
    public string Currency { get; set; } = "BDT";

    public List<AdmissionFormFieldDto> Fields { get; set; } = new();
    public List<AdmissionDocumentRequirementDto> DocumentRequirements { get; set; } = new();
}

public sealed class CreateAdmissionIntakeFormDto : AdmissionIntakeFormInputDto
{
    public Guid ClientRequestId { get; set; }
}

public sealed class UpdateAdmissionIntakeFormDto : AdmissionIntakeFormInputDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class AdmissionFormFieldDto
{
    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_]*$")]
    public string Key { get; set; } = string.Empty;

    [Required, StringLength(200)] public string Label { get; set; } = string.Empty;
    [StringLength(200)] public string? LabelBangla { get; set; }
    public AdmissionFormFieldType Type { get; set; }
    public bool IsRequired { get; set; }
    [Range(1, 4000)] public int? MaxLength { get; set; }
    public int DisplayOrder { get; set; }
    public List<string> Options { get; set; } = new();
}

public sealed class AdmissionDocumentRequirementDto
{
    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_-]*$")]
    public string DocumentType { get; set; } = string.Empty;

    [Required, StringLength(200)] public string Label { get; set; } = string.Empty;
    [StringLength(200)] public string? LabelBangla { get; set; }
    public bool IsRequired { get; set; }
    [Range(1, 10)] public int MaxFileSizeMb { get; set; } = 5;
    [Required, MinLength(1)] public List<string> AllowedExtensions { get; set; } = new();
}

public sealed class AdmissionIntakeFormDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long CampusId { get; set; }
    public long AcademicUnitId { get; set; }
    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }
    public decimal ApplicationFee { get; set; }
    public string Currency { get; set; } = string.Empty;
    public AdmissionIntakeFormStatus Status { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public List<AdmissionFormFieldDto> Fields { get; set; } = new();
    public List<AdmissionDocumentRequirementDto> DocumentRequirements { get; set; } = new();
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PublicAdmissionIntakeFormDto
{
    public Guid Reference { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long CampusId { get; set; }
    public long AcademicUnitId { get; set; }
    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }
    public decimal ApplicationFee { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<AdmissionFormFieldDto> Fields { get; set; } = new();
    public List<AdmissionDocumentRequirementDto> DocumentRequirements { get; set; } = new();
}

public sealed class AdmissionDocumentUploadDto
{
    public Guid ClientRequestId { get; set; }
    [Required, StringLength(30, MinimumLength = 8)] public string Mobile { get; set; } = string.Empty;
    [Required, StringLength(50)] public string DocumentType { get; set; } = string.Empty;
    [Required] public IFormFile? File { get; set; }
}

public sealed class AdmissionApplicantDocumentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public AdmissionDocumentVerificationStatus VerificationStatus { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReviewAdmissionDocumentDto
{
    public AdmissionDocumentVerificationStatus Status { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class AdmissionRowVersionDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class AdmissionDocumentContentDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
    public string FileName { get; set; } = "document";
}
