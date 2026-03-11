namespace EduOS.Core.DTOs.Auth
{
    public class CommonResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
    public class ResendVerificationDto
    {
        public string Email { get; set; } = string.Empty;
    }
}