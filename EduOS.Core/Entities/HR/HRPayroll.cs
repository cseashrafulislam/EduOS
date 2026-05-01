using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class HRPayroll : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public DateTime SalaryMonth { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal Deduction { get; set; }
        public decimal NetSalary { get; set; }
        public string Status { get; set; }
    }
}
