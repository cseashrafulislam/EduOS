using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth
{
    public class TwoFactorAuth : BaseEntity
    {
        public long UserId { get; set; }
        public string Method { get; set; } = "App"; // SMS/Email/App
        public string SecretKey { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = false;
        public string? BackupCodes { get; set; } // JSON array

        public virtual ApplicationUser? User { get; set; }
    }
}
