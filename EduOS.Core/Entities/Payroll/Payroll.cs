using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Payroll
{
    public class Payroll : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public string Month { get; set; } = string.Empty;
        public int Year { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal Deductions { get; set; } = 0;
        public decimal Bonus { get; set; } = 0;
        public decimal NetSalary { get; set; }
        public DateTime? PaymentDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending/Paid
        public string? PaymentMethod { get; set; }
        public string? Note { get; set; }

        public virtual Employee? Employee { get; set; }
    }
}
