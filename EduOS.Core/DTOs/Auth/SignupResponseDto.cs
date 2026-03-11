namespace EduOS.Core.DTOs.Auth
{
    public class SignupResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public int? TenantId { get; set; }
        public int? UserId { get; set; }
        public string? VerificationToken { get; set; }
    }
}