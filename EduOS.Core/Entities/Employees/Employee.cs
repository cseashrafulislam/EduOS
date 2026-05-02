using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Employees
{
    public class Employee : BaseTenantEntity
    {
        public long? UserId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? FatherName { get; set; }
        public string? MotherName { get; set; }
        public DateTime? DOB { get; set; }
        public string? Gender { get; set; }
        public string? BloodGroup { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? NID { get; set; }
        public int DesignationId { get; set; }
        public int? DepartmentId { get; set; }
        public DateTime JoiningDate { get; set; }
        public decimal Salary { get; set; }
        public string? PhotoUrl { get; set; }
        public string? Qualification { get; set; }
        public string? Experience { get; set; }
        public bool IsTeacher { get; set; } = false;
        public bool IsActive { get; set; } = true;

        public virtual ApplicationUser? User { get; set; }
        public virtual Designation? Designation { get; set; }
        public virtual Department? Department { get; set; }
    }
}
