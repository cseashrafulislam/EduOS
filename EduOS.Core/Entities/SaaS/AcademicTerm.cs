using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class AcademicTerm : TenantEntity
    {
        public long AcademicYearId { get; set; }

        public string Name { get; set; } = string.Empty; // Term 1, Semester 1
        public string? TermType { get; set; } // Term / Semester / Quarter / Session

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsCurrent { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }
}