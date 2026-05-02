using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth
{
    public class LoginHistory : BaseEntity
    {
        public long UserId { get; set; }
        public DateTime LoginAt { get; set; }
        public DateTime? LogoutAt { get; set; }
        public long TenantId { get; set; }
        public string UserAgent { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? Browser { get; set; }
        public string? Device { get; set; }
        public string? Location { get; set; }
        public bool IsSuccess { get; set; }
        public string? FailReason { get; set; }

        public virtual ApplicationUser? User { get; set; }
    
    }
}
