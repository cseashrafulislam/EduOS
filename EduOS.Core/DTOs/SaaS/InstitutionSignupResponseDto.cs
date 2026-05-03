namespace EduOS.Core.DTOs.SaaS
{
    public class InstitutionSignupResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Email { get; set; }
        public long? TenantId { get; set; }
        public long? UserId { get; set; }
    }
}