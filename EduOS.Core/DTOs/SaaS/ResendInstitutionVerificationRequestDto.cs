namespace EduOS.Core.DTOs.SaaS;

/// <summary>
/// Anonymous recovery request. The response never reveals whether an institution or user exists.
/// </summary>
public sealed class ResendInstitutionVerificationRequestDto
{
    public string? Email { get; set; }
}
