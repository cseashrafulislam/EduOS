namespace EduOS.Core.DTOs.Student;

/// <summary>Historical transfer certificate projection for an authorized source-tenant administrator.</summary>
public sealed class StudentTransferCertificateDto
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string CertificateNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    // False for pre-migration certificates: identity is a current-directory fallback.
    public bool IdentityFromIssueSnapshot { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long AcademicProgramId { get; set; }
    public string? Reason { get; set; }
    public string? ConductRemark { get; set; }
    public bool FeesCleared { get; set; }
}
