using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class Notification : BaseTenantEntity
    {
        public int RecipientUserId { get; set; }
        public string Type { get; set; } = "InApp"; // SMS/Email/Push/InApp
        public string? Subject { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending/Sent/Failed/Read
        public DateTime? SentAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public bool IsRead { get; set; } = false;
        public string? RelatedEntityType { get; set; }
        public int? RelatedEntityId { get; set; }

        public virtual User? Recipient { get; set; }
    }
}
