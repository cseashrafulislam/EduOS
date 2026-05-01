using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class SalaryStructure : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal HouseRent { get; set; }
        public decimal MedicalAllowance { get; set; }
        public decimal OtherAllowance { get; set; }
    }
}
