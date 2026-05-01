using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Payroll
{
    public class Bonus : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public string BonusType { get; set; } = "Festival"; // Festival/Performance
        public decimal Amount { get; set; }
        public string BonusMonth { get; set; } = string.Empty;
        public int BonusYear { get; set; }
        public string? Reason { get; set; }
        public DateTime? PaidDate { get; set; }

        public virtual Employee? Employee { get; set; }
    }
}
