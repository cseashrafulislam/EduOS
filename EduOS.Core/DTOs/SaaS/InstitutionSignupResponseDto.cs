namespace EduOS.Core.DTOs.SaaS
{
    public class InstitutionSignupResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public long? TenantId { get; set; }
        public int? UserId { get; set; }
    }
}