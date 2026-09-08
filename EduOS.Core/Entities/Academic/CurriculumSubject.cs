using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduOS.Core.Entities.Academic
{
    public class CurriculumSubject : BaseTenantEntity
    {
        public long AcademicCurriculumId { get; set; }
        public long AcademicLevelId { get; set; }
        public long SubjectId { get; set; }

        public long? AcademicTrackId { get; set; }
        public long? MediumId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FullMarks { get; set; } = 100;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PassMarks { get; set; } = 33;

        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditHours { get; set; }

        public bool IsOptional { get; set; }
        public bool HasPractical { get; set; }

        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;

        public virtual AcademicCurriculum? AcademicCurriculum { get; set; }
        public virtual AcademicLevel? AcademicLevel { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual AcademicTrack? AcademicTrack { get; set; }
        public virtual Medium? Medium { get; set; }
    }
}