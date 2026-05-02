using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class DeviceToken : BaseTenantEntity
    {
        public long UserId { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string FcmToken { get; set; } = string.Empty;
        public string DeviceType { get; set; } = "Android"; // Android/iOS
        public string? AppVersion { get; set; }
        public DateTime LastActive { get; set; }

        public virtual ApplicationUser? User { get; set; }
    }
}
