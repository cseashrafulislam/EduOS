namespace EduOS.Core.DTOs.Auth
{
    public class LoginRequestDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public class LoginResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public long userId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int TenantId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;
        public IList<string> Roles { get; set; } = new List<string>();
        public bool IsOnboardingComplete { get; set; }
        public int CurrentOnboardingStep { get; set; }
    }
    public class RefreshTokenRequestDto
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
    public class LogoutRequestDto
    {
        public string? RefreshToken { get; set; }
    }
    public class ForgotPasswordRequestDto
    {
        public string Email { get; set; } = "";
    }

    public class ResetPasswordRequestDto
    {
        public string Email { get; set; } = "";
        public string Token { get; set; } = "";

        public string NewPassword { get; set; } = "";
        public string ConfirmPassword { get; set; } = "";
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
