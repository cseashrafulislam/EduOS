using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Finance
{
    public class StudentDiscount : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int DiscountId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Reason { get; set; }
        public int ApprovedBy { get; set; }

        public virtual Student? Student { get; set; }
        public virtual Discount? Discount { get; set; }
    }
}
