using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class MessageQueue : BaseTenantEntity
    {
        public string Type { get; set; } = "SMS"; // SMS/Email/Push
        public string Recipient { get; set; } = string.Empty; // Phone/Email
        public string? Subject { get; set; }
        public string Body { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending/Sent/Failed
        public int RetryCount { get; set; } = 0;
        public DateTime? ScheduledAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
