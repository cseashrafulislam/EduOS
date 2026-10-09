namespace EduOS.Core.DTOs.Auth;

public sealed class ChangePasswordRequestDto
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required, MinLength(6)] public string NewPassword { get; set; } = string.Empty;
    [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ResendVerificationRequestDto
{
    [Required, EmailAddress, MaxLength(320)] public string Email { get; set; } = string.Empty;
}
