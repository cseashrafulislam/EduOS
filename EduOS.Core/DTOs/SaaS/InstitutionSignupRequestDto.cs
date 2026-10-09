namespace EduOS.Core.DTOs.SaaS;

public sealed class InstitutionSignupRequestDto
{
    /// <summary>Server-validated idempotency key. Must be unique for an identical signup request.</summary>
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(200)] public string InstitutionName { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string OwnerName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(320)] public string Email { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [Required, MinLength(6), MaxLength(128)] public string Password { get; set; } = string.Empty;
    [Required, Compare(nameof(Password))] public string ConfirmPassword { get; set; } = string.Empty;
    public long? InstitutionTypeDefinitionId { get; set; }
    /// <summary>Server must reject a false value; record versioned terms acceptance separately.</summary>
    public bool AgreeTerms { get; set; }
}
