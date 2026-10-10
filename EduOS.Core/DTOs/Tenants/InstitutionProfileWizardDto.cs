namespace EduOS.Core.DTOs.Tenants;

/// <summary>Legacy onboarding form contract; authoritative institution fields live in Tenant.</summary>
public sealed class InstitutionProfileWizardDto
{
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? OwnerEmail { get; set; }
    public string? OwnerPhone { get; set; }
    public string? OwnerDesignation { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
