using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class StudentFee : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int FeeTypeId { get; set; }
        public decimal Amount { get; set; }
        public DateTime DueDate { get; set; }
    }
}
