using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class Department : BaseTenantEntity
    {
        public long? CampusId { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ShortName { get; set; }

        public long? HeadEmployeeId { get; set; }

        [MaxLength(200)]
        public string? HeadOfDepartment { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }
}
