using Microsoft.AspNetCore.Identity;

namespace EduOS.Core.Entities.Auth
{
    public class ApplicationUser : IdentityUser<int>
    {
        public string FullName { get; set; } = string.Empty;
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;
        public int? TenantId { get; set; }
    }
}