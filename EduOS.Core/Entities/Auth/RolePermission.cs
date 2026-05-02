using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth
{
    public class RolePermission : BaseEntity
    {
        public long RoleId { get; set; }
        public long PermissionId { get; set; }

        public virtual ApplicationRole? Role { get; set; }
        public virtual Permission? Permission { get; set; }
    }
}
