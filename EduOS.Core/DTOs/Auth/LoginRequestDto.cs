namespace EduOS.Core.DTOs.Auth
{
    public class LoginRequestDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
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
}