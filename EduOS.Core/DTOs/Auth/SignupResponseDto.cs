namespace EduOS.Core.DTOs.Auth
{
    public class SignupResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public long? TenantId { get; set; }
        public long? UserId { get; set; }
        public string? VerificationToken { get; set; }
    }
}