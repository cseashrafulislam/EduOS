using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class CustomFieldValue : BaseEntity
    {
        public long CustomFieldId { get; set; }
        public long EntityId { get; set; } // Student/Employee Id
        public string? Value { get; set; }

        public virtual CustomField? CustomField { get; set; }
    }
}
