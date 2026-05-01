using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth
{
    public class LoginHistory : BaseEntity
    {
        public int UserId { get; set; }
        public DateTime LoginTime { get; set; }
        public DateTime? LogoutTime { get; set; }
        public string? IpAddress { get; set; }
        public string? Browser { get; set; }
        public string? Device { get; set; }
        public string? Location { get; set; }
        public string Status { get; set; } = "Success"; // Success/Failed

        public virtual User? User { get; set; }
    }
}
