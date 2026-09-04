using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class AcademicTrack : BaseTenantEntity
    {
        public long? AcademicProgramId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;

        public virtual AcademicProgram? AcademicProgram { get; set; }
    }
}