using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class CustomFieldValue : BaseEntity
    {
        public int CustomFieldId { get; set; }
        public int EntityId { get; set; } // Student/Employee Id
        public string? Value { get; set; }

        public virtual CustomField? CustomField { get; set; }
    }
}
