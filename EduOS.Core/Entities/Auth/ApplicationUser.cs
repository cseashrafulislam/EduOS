using EduOS.Core.Entities.Tenants;
using Microsoft.AspNetCore.Identity;

namespace EduOS.Core.Entities.Auth
{
    public class ApplicationUser : IdentityUser<long>
    {
        // ==================== Tenant ====================
        public long? TenantId { get; set; }

        // ==================== Profile ====================

        public string FullName { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhotoUrl { get; set; }
        public string? Address { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }

        // ==================== User Type ====================

        /// <summary>
        /// Business role type (separate from Identity Role).
        /// Examples: SuperAdmin, TenantAdmin, Principal, Teacher, Student, Parent,
        /// Accountant, HR, Librarian, Staff
        /// </summary>
        public string UserType { get; set; } = string.Empty;

        // ==================== Status ====================

        public bool IsActive { get; set; } = true;
        public DateTime? LastLogin { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public string? LastLoginIp { get; set; }

        // ==================== Refresh Token (for JWT/API support) ====================

        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiry { get; set; }

        // ==================== Audit ====================

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public long? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; } = false;

        // ==================== Navigation ====================

        public virtual Tenant? Tenant { get; set; }
    }
}
