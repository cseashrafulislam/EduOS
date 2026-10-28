namespace EduOS.Core.DTOs.Auth;

public sealed class MfaSecurityActionDto
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required] public string Code { get; set; } = string.Empty;
    public bool UseRecoveryCode { get; set; }
}

public sealed class RevokeUserSessionDto
{
    [Required, StringLength(64, MinimumLength = 64)]
    public string SessionId { get; set; } = string.Empty; // public reference, never cookie SID
    [StringLength(256)] public string? CurrentPassword { get; set; }
}
