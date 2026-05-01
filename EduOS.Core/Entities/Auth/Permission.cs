using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth
{
    public class Permission : BaseEntity
    {
        public string Name { get; set; } = string.Empty; // student.view/student.create
        public string Module { get; set; } = string.Empty;
        public string? Description { get; set; }

        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }
}
