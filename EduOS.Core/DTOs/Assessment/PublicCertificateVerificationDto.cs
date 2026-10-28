namespace EduOS.Core.DTOs.Assessment;

/// <summary>Public, bearer-reference-only credential status. Never includes learner, campus or enrollment PII.</summary>
public sealed class PublicCertificateVerificationDto
{
    public string IssuerName { get; set; } = string.Empty;
    public string CertificateNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public string Status { get; set; } = string.Empty;
}
