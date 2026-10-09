namespace EduOS.Core.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public class LoginResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        /// <summary>When true, no access/refresh tokens have been issued.</summary>
        public bool RequiresMfa { get; set; }
        public string? MfaChallengeToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public long UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public long TenantId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;
        public IList<string> Roles { get; set; } = new List<string>();
        public EduOS.Core.Enums.Domain.OnboardingStage OnboardingStage { get; set; }
    }
    public class RefreshTokenRequestDto
    {
        [Required] public string RefreshToken { get; set; } = string.Empty;
    }
    public class LogoutRequestDto
    {
        public string? RefreshToken { get; set; }
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
        public DateTime IssuedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
