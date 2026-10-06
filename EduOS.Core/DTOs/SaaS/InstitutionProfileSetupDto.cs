using System;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS
{
    public class InstitutionProfileSetupDto
    {
        [Required, MaxLength(200)]
        public string InstitutionName { get; set; } = string.Empty;
        [Required]
        [MaxLength(50)]
        public string? InstitutionType { get; set; }
        [Required, MaxLength(150)]
        public string OwnerName { get; set; } = string.Empty;
        [MaxLength(20)]
        public string? OwnerPhone { get; set; }
        [EmailAddress, MaxLength(150)]
        public string? OwnerEmail { get; set; }
        [MaxLength(100)]
        public string? OwnerDesignation { get; set; }
        [MaxLength(20)]
        public string? Phone { get; set; }
        [Url, MaxLength(200)]
        public string? Website { get; set; }
        [MaxLength(500)]
        public string? Address { get; set; }
        [MaxLength(100)]
        public string? City { get; set; }
        [MaxLength(100)]
        public string? State { get; set; }
        [MaxLength(100)]
        public string? Country { get; set; }
        [MaxLength(20)]
        public string? PostalCode { get; set; }
        [EmailAddress, MaxLength(150)]
        public string Email { get; set; } = string.Empty;
    }
}
