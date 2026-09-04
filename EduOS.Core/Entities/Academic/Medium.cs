using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class Medium : BaseTenantEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }
}