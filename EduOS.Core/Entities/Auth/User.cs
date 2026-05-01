using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Tenants;

namespace EduOS.Core.Entities.Auth
{
    public class User : BaseEntity
    {
        public int? TenantId { get; set; } // null for SuperAdmin
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty; // SuperAdmin/TenantAdmin/Principal/Teacher/Student/Parent/Accountant/HR/Librarian/Staff
        public string? PhotoUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsEmailVerified { get; set; } = false;
        public bool IsPhoneVerified { get; set; } = false;
        public DateTime? LastLogin { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiry { get; set; }
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockedUntil { get; set; }

        public virtual Tenant? Tenant { get; set; }
        public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}
