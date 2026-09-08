using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class AcademicYear : BaseTenantEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsCurrent { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual ICollection<AcademicTerm> Terms { get; set; } = new List<AcademicTerm>();
    }
}