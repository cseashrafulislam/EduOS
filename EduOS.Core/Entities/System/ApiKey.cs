using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class ApiKey : BaseTenantEntity
    {
        public string KeyName { get; set; } = string.Empty;
        public string ApiKeyValue { get; set; } = string.Empty;
        public string? Permissions { get; set; } // JSON
        public DateTime? ExpiryDate { get; set; }
        public DateTime? LastUsed { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
