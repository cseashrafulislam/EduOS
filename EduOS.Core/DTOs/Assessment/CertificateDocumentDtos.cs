namespace EduOS.Core.DTOs.Assessment;

/// <summary>Authenticated, printable certificate facts captured at issuance. No mutable student fields are re-read for display.</summary>
public sealed class CertificateSnapshotDocumentDto
{
    public int SchemaVersion { get; set; } = 1;
    public long TenantId { get; set; }
    public string IssuerName { get; set; } = string.Empty;
    public long StudentId { get; set; }
    public Guid StudentReference { get; set; }
    public long StudentEnrollmentId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid CertificateReference { get; set; }
    public string CertificateNumber { get; set; } = string.Empty;
    public long CertificateTemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string RenderedBody { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public long ResultPublicationId { get; set; }
    public int ResultPublicationVersionNo { get; set; }
    public DateOnly IssueDate { get; set; }
    public long IssuedByUserId { get; set; }
    public DateTime IssuedAtUtc { get; set; }
}
