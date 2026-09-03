using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS
{
    public class CampusSetupDto
    {
        public long? Id { get; set; }
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;
        [MaxLength(50)]
        public string? Code { get; set; }
        [MaxLength(500)]
        public string? Address { get; set; }
        [MaxLength(20)]
        public string? Phone { get; set; }
        [EmailAddress, MaxLength(150)]
        public string? Email { get; set; }
        [MaxLength(150)]
        public string? HeadName { get; set; }
        public bool IsHeadOffice { get; set; }
    }
}
