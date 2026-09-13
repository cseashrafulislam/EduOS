using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.System
{
    public class IdCard : BaseTenantEntity
    {
        public long? StudentId { get; set; }
        public long? EmployeeId { get; set; }
        public string CardNo { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public long? TemplateId { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Student? Student { get; set; }
        public virtual Employee? Employee { get; set; }
        public virtual DocumentTemplate? Template { get; set; }
    }
}
