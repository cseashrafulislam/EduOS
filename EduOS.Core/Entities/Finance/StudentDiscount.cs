using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Finance
{
    public class StudentDiscount : BaseTenantEntity
    {
        public long StudentId { get; set; }
        public long DiscountId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Reason { get; set; }
        public long ApprovedBy { get; set; }
        public virtual Student? Student { get; set; }
        public virtual Discount? Discount { get; set; }
    }
}
