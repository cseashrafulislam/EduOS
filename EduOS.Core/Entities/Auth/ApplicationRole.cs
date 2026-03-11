using Microsoft.AspNetCore.Identity;

namespace EduOS.Core.Entities.Auth
{
    public class ApplicationRole : IdentityRole<int>
    {
        public long? TenantId { get; set; }
        public bool IsSystemRole { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public string? Description { get; set; }
    }
}