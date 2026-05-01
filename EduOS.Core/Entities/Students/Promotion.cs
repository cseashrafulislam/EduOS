using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class Promotion : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int FromClassId { get; set; }
        public int ToClassId { get; set; }
        public int FromYearId { get; set; }
        public int ToYearId { get; set; }
        public string Status { get; set; } = "Promoted"; // Promoted/Repeated
        public DateTime PromotionDate { get; set; }

        public virtual Student? Student { get; set; }
        public virtual Class? FromClass { get; set; }
        public virtual Class? ToClass { get; set; }
        public virtual AcademicYear? FromYear { get; set; }
        public virtual AcademicYear? ToYear { get; set; }
    }
}
