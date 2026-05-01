using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class NotificationPreference : BaseEntity
    {
        public int UserId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public bool EmailEnabled { get; set; } = true;
        public bool SmsEnabled { get; set; } = true;
        public bool PushEnabled { get; set; } = true;
        public bool InAppEnabled { get; set; } = true;

        public virtual User? User { get; set; }
    }
}
