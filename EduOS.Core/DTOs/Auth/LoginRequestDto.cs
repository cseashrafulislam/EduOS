namespace EduOS.Core.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "Username or email is required.")]
        [StringLength(256)]
        public string UserNameOrEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
        // Optional explicit institution for multi-membership accounts.
        public long? TenantId { get; set; }

    }

    public class ForgotPasswordRequestDto
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
    }

    public class ResetPasswordRequestDto
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Token { get; set; } = "";

        [Required, MinLength(6)] public string NewPassword { get; set; } = "";
        [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = "";
    }

    public class MfaSetupRequestDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
    }

    public class MfaEnableRequestDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class MfaLoginRequestDto
    {
        public string ChallengeToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public bool UseRecoveryCode { get; set; }
    }

    public class MfaChallengeData
    {
        public long UserId { get; set; }
        public string SecurityStamp { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
        public long? TenantId { get; set; }
        public DateTime IssuedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
