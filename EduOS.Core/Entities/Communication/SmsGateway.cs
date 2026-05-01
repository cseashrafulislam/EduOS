using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class SmsGateway : BaseTenantEntity
    {
        public string Provider { get; set; } = string.Empty; // BulkSMS/SSL/Mim
        public string? ApiUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? SenderId { get; set; }
        public decimal Balance { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDefault { get; set; } = false;
    }
}
