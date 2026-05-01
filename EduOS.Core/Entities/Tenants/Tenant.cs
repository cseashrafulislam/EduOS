using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Entities.Tenants
{
    public class Tenant : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
        public string? CustomDomain { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? LogoUrl { get; set; }
        public string? EIIN { get; set; }
        public string TenantType { get; set; } = "School"; // School/College/University/Coaching
        public bool IsActive { get; set; } = true;

        // Navigation
        public virtual ICollection<TenantSetting> Settings { get; set; } = new List<TenantSetting>();
        public virtual ICollection<Campus> Campuses { get; set; } = new List<Campus>();
        public virtual ICollection<User> Users { get; set; } = new List<User>();
        public virtual TenantSubscription? Subscription { get; set; }
    }
}
