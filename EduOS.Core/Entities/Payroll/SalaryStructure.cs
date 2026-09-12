using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;
namespace EduOS.Core.Entities.Payroll
{
    public class SalaryStructure : BaseTenantEntity
    {
        public long EmployeeId { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal HouseRent { get; set; }
        public decimal Medical { get; set; }
        public decimal Transport { get; set; }
        public decimal Others { get; set; }
        public decimal GrossSalary { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public bool IsActive { get; set; } = true;
        public virtual Employee? Employee { get; set; }
    }
}
