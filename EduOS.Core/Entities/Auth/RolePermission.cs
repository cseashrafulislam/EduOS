using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth
{
    public class RolePermission : BaseEntity
    {
        public int RoleId { get; set; }
        public int PermissionId { get; set; }

        public virtual Role? Role { get; set; }
        public virtual Permission? Permission { get; set; }
    }
}
