using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class CustomField : BaseTenantEntity
    {
        public string EntityType { get; set; } = string.Empty; // Student/Employee
        public string FieldName { get; set; } = string.Empty;
        public string FieldType { get; set; } = "Text"; // Text/Number/Date/Dropdown
        public string? Options { get; set; }
        public bool IsRequired { get; set; } = false;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
