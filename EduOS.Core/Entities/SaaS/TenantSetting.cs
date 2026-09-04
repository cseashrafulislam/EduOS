using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    /// <summary>
    /// Generic key-value settings store for each tenant.
    /// Used for things that don't deserve their own column (SMS API key, custom flags, etc.).
    /// </summary>
    public class TenantSetting : BaseEntity
    {
        public long TenantId { get; set; }

        public string Category { get; set; } = string.Empty; // e.g. "Branding", "Sms", "Email"
        public string SettingKey { get; set; } = string.Empty; // e.g. "PrimaryColor"
        public string? SettingValue { get; set; }
        public string DataType { get; set; } = "string"; // string, int, bool, json

        public string? Description { get; set; }

        /// <summary>
        /// True for things like API keys, passwords (should be encrypted)
        /// </summary>
        public bool IsSensitive { get; set; }

        /// <summary>
        /// True if user can edit this. False for system-managed.
        /// </summary>
        public bool IsEditable { get; set; } = true;

        // ==================== Navigation ====================

        public virtual Tenant? Tenant { get; set; }
    }
}
