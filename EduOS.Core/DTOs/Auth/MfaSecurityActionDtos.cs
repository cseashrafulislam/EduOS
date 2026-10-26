namespace EduOS.Core.DTOs.Auth;

public sealed class MfaSecurityActionDto
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required] public string Code { get; set; } = string.Empty;
    public bool UseRecoveryCode { get; set; }
}

public sealed class RevokeUserSessionDto
{
    [Required, StringLength(48, MinimumLength = 48)]
    public string SessionId { get; set; } = string.Empty;
}
