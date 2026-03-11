using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class Feature : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;   // Student Management
        public string Code { get; set; } = string.Empty;   // STUDENT_MANAGEMENT
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}