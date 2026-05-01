using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class DocumentTemplate : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // IDCard/Certificate/Receipt/Admit
        public string HtmlContent { get; set; } = string.Empty;
        public string? FieldsJson { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
