using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth
{
    public class Role : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsSystemRole { get; set; } = false; // Pre-defined roles can't be deleted
        public bool IsActive { get; set; } = true;

        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
        public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}
