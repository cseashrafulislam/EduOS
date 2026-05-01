using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class WebhookEndpoint : BaseTenantEntity
    {
        public string Url { get; set; } = string.Empty;
        public string? EventTypes { get; set; } // JSON array
        public string? Secret { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
