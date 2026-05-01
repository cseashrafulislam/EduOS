using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Tenants
{
    public class TenantSetting : BaseTenantEntity
    {
        public string SettingKey { get; set; } = string.Empty;
        public string? SettingValue { get; set; }
        public string Category { get; set; } = "General"; // General/Academic/Finance/Notification/Branding/Integration
        public string DataType { get; set; } = "string"; // string/int/bool/json
    }
}
