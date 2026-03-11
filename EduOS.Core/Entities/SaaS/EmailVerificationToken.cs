using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class EmailVerificationToken : BaseAuditableEntity
    {
        public int UserId { get; set; }

        // If you want, later navigation add করতে পারো:
        // public ApplicationUser User { get; set; } = null!;

        public string Email { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;

        public DateTime ExpireAt { get; set; }
        public DateTime? VerifiedAt { get; set; }

        public bool IsUsed { get; set; } = false;
        public bool IsExpired => DateTime.UtcNow > ExpireAt;
    }
}