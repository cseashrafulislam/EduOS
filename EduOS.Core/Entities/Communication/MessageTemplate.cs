using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class MessageTemplate : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "SMS"; // SMS/Email
        public string? Subject { get; set; }
        public string Body { get; set; } = string.Empty;
        public string? Event { get; set; } // Trigger event
        public bool IsActive { get; set; } = true;
    }
}
