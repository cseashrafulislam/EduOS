using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class HREmployee : BaseTenantEntity
    {
        public string EmployeeCode { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public int? DepartmentId { get; set; }
        public int? DesignationId { get; set; }
        public DateTime? JoinDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
