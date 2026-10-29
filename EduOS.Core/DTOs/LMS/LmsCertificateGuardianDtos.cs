using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.LMS;

public sealed class LmsGuardianChoiceDto
{
    public long GuardianLinkId { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public string RelationCode { get; set; } = string.Empty;
}
public sealed class LmsCertificateGuardianGrantDto
{
    public Guid Reference { get; set; }
    public long GuardianLinkId { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class GrantLmsCertificateGuardianRequestDto
{
    public Guid CertificateReference { get; set; }
    public long GuardianLinkId { get; set; }
    public Guid ClientRequestId { get; set; }
}
public sealed class RevokeLmsCertificateGuardianRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
