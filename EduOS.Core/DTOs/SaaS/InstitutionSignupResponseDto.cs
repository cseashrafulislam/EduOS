namespace EduOS.Core.DTOs.SaaS;

/// <summary>Registration outcome. Success and diagnostic messages belong to ApiResponse, not this data DTO.</summary>
public sealed class InstitutionSignupResponseDto
{
    public Guid TenantReference { get; set; }
    public Guid UserReference { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool EmailVerificationRequired { get; set; } = true;
}
