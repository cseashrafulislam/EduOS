using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.HR
{
    public class SalaryStructure : TenantEntity
    {
        public int EmployeeId { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal HouseRent { get; set; }
        public decimal MedicalAllowance { get; set; }
        public decimal OtherAllowance { get; set; }
    }
}
