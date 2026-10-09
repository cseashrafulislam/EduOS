namespace EduOS.Core.DTOs.SaaS;

/// <summary>Checks the availability of a platform-issued tenant subdomain, not custom-domain DNS ownership.</summary>
public sealed class SubdomainAvailabilityDto
{
    public string Subdomain { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public bool IsAvailable { get; set; }
    public string? Reason { get; set; }
}

public sealed class UpdateTenantSubdomainRequestDto
{
    [Required, StringLength(100, MinimumLength = 3)]
    [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    public string Subdomain { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class TenantDomainDto
{
    public long Id { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string VerificationStateCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
