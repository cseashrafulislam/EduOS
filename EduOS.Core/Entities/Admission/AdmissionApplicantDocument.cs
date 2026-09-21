using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Admission;

public sealed class AdmissionApplicantDocument : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long ApplicantId { get; set; }
    public long AdmissionIntakeFormId { get; set; }

    [MaxLength(50)]
    public string DocumentType { get; set; } = string.Empty;

    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string StorageKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [MaxLength(44)]
    public string Sha256 { get; set; } = string.Empty;

    public AdmissionDocumentVerificationStatus VerificationStatus { get; set; } = AdmissionDocumentVerificationStatus.Pending;
    public bool IsCurrent { get; set; } = true;
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }

    [MaxLength(1000)]
    public string? ReviewNote { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual AdmissionApplicant? Applicant { get; set; }
    public virtual AdmissionIntakeForm? AdmissionIntakeForm { get; set; }
}
