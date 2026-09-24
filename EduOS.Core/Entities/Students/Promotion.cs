using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class Promotion : BaseTenantEntity
    {
        public long StudentId { get; set; }
        public long FromClassId { get; set; }
        public long ToClassId { get; set; }
        public long FromYearId { get; set; }
        public long ToYearId { get; set; }
        public string Status { get; set; } = "Promoted"; // Promoted/Repeated
        public DateTime PromotionDate { get; set; }

        public virtual Student? Student { get; set; }
        public virtual Class? FromClass { get; set; }
        public virtual Class? ToClass { get; set; }
        public virtual AcademicYear? FromYear { get; set; }
        public virtual AcademicYear? ToYear { get; set; }
    }
}
