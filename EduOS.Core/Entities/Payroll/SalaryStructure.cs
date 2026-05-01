using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Payroll
{
    public class SalaryStructure : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal HouseRent { get; set; } = 0;
        public decimal Medical { get; set; } = 0;
        public decimal Transport { get; set; } = 0;
        public decimal Others { get; set; } = 0;
        public decimal GrossSalary { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Employee? Employee { get; set; }
    }
}
