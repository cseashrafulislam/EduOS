namespace EduOS.Core.DTOs.SaaS
{
    public class InstitutionSignupRequestDto
    {
        public string InstitutionName { get; set; } = string.Empty;
        public string InstitutionType { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Password { get; set; } = string.Empty;

        public string? Address { get; set; }
    }
}