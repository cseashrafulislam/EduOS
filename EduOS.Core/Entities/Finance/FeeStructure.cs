using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class FeeStructure : BaseTenantEntity
    {
        public int AcademicYearId { get; set; }
        public int ClassId { get; set; }
        public int FeeHeadId { get; set; }
        public decimal Amount { get; set; }

        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual Class? Class { get; set; }
        public virtual FeeHead? FeeHead { get; set; }
    }
}
