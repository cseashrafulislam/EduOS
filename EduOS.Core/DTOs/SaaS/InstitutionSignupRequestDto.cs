using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS
{
    public class InstitutionSignupRequestDto
    {
        [Required]
        public string InstitutionName { get; set; } = string.Empty;

        [Required]
        public string OwnerName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;

        public string? InstitutionType { get; set; }

        public bool AgreeTerms { get; set; }
    }
}