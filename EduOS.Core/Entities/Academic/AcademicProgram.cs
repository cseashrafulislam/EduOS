using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class AcademicProgram : BaseTenantEntity
    {
        public long? CampusId { get; set; }
        public long? DepartmentId { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ShortName { get; set; }

        public int DurationInMonths { get; set; }

        [MaxLength(150)]
        public string? AwardTitle { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        public bool IsAdmissionOpen { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;

        public virtual Department? Department { get; set; }
        public virtual ICollection<AcademicLevel> Levels { get; set; } = new List<AcademicLevel>();
    }
}