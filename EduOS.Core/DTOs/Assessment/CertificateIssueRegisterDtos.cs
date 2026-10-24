using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class CertificateIssueRegisterQueryDto
{
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed class CertificateCandidateDto
{
    public Guid StudentReference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
}

public sealed class CertificateIssueRegisterRowDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public string CertificateNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CertificateIssueRegisterPageDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<CertificateIssueRegisterRowDto> Items { get; set; } =
        Array.Empty<CertificateIssueRegisterRowDto>();
}
