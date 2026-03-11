using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Finance
{
    public class StudentFee : TenantEntity
    {
        public int StudentId { get; set; }
        public int FeeTypeId { get; set; }
        public decimal Amount { get; set; }
        public DateTime DueDate { get; set; }
    }
}
