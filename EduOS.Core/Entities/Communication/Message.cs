using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class Message : BaseTenantEntity
    {
        public long SenderId { get; set; }
        public long ReceiverId { get; set; }
        public string? Subject { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime SentAt { get; set; }
        public DateTime? ReadAt { get; set; }

        public virtual ApplicationUser? Sender { get; set; }
        public virtual ApplicationUser? Receiver { get; set; }
    }
}
