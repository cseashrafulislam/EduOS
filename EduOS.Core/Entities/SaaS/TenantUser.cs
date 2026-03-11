using EduOS.Core.Entities.Common;
using EduOS.Core.Entities.Auth;

namespace EduOS.Core.Entities.SaaS
{
    public class TenantUser : TenantEntity
    {
        public int UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        public bool IsOwner { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }
}