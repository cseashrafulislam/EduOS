using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class FeeStructure : BaseTenantEntity
    {
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public long FeeHeadId { get; set; }
        public decimal Amount { get; set; }

        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual Class? Class { get; set; }
        public virtual FeeHead? FeeHead { get; set; }
    }
}
