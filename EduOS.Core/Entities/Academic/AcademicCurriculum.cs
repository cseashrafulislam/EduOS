using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class AcademicCurriculum : BaseTenantEntity
    {
        public long AcademicProgramId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public long? EffectiveFromAcademicYearId { get; set; }
        public long? EffectiveToAcademicYearId { get; set; }

        public bool IsCurrent { get; set; }
        public bool IsActive { get; set; } = true;

        [MaxLength(500)]
        public string? Remarks { get; set; }

        public virtual AcademicProgram? AcademicProgram { get; set; }

        public virtual ICollection<CurriculumSubject> Subjects { get; set; }
            = new List<CurriculumSubject>();
    }
}