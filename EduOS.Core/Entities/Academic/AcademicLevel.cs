using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class AcademicLevel : BaseTenantEntity
    {
        public long AcademicProgramId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public int LevelNo { get; set; } = 1;

        public bool IsPromotable { get; set; } = true;
        public bool IsTerminalLevel { get; set; }
        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; } = 1;

        public virtual AcademicProgram? AcademicProgram { get; set; }
    }
}