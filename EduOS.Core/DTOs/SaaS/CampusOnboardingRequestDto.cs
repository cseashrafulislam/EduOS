namespace EduOS.Core.DTOs.SaaS;

/// <summary>Campus onboarding omits the canonical required code; the server allocates it.</summary>
public sealed class CampusOnboardingRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? Code { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(150)] public string? HeadName { get; set; }
    public bool IsHeadOffice { get; set; }
    public bool IsActive { get; set; } = true;
    public bool? IsOnlineCampus { get; set; }
    [MaxLength(100)] public string? TimeZoneId { get; set; }
    public string? RowVersion { get; set; }
}
