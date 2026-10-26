namespace EduOS.Core.DTOs.SaaS;

/// <summary>Tenant-owned profile fields and the separately owned administrator/profile settings.</summary>
public sealed class TenantOnboardingProfileDto
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public long? InstitutionTypeDefinitionId { get; set; }
    [MaxLength(250)] public string? LegalName { get; set; }
    [MaxLength(100)] public string? RegistrationNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Subdomain { get; set; }
    [Required, MaxLength(200)] public string OwnerName { get; set; } = string.Empty;
    [EmailAddress, MaxLength(200)] public string? OwnerEmail { get; set; }
    [MaxLength(30)] public string? OwnerPhone { get; set; }
    [MaxLength(150)] public string? OwnerDesignation { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    [Required, MaxLength(10)] public string CountryCode { get; set; } = "BD";
    [MaxLength(250)] public string? Website { get; set; }
    [MaxLength(150)] public string? City { get; set; }
    [MaxLength(150)] public string? State { get; set; }
    [MaxLength(150)] public string? Country { get; set; }
    [MaxLength(50)] public string? PostalCode { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
