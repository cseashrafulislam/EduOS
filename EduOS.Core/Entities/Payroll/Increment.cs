using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Payroll
{
    public class Increment : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public decimal OldSalary { get; set; }
        public decimal NewSalary { get; set; }
        public decimal IncrementAmount { get; set; }
        public DateTime EffectiveDate { get; set; }
        public string? Reason { get; set; }
        public int ApprovedBy { get; set; }

        public virtual Employee? Employee { get; set; }
    }
}
