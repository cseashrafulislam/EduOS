using Microsoft.AspNetCore.Identity;

namespace EduOS.Core.Entities.Auth
{
    public class ApplicationRole : IdentityRole<long>
    {
        public string? Description { get; set; }

        public long? TenantId { get; set; }
        public bool IsSystemRole { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
    }
}
