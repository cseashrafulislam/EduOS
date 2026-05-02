using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Tenants;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.Entities.Auth
{
    public class TenantUser : BaseTenantEntity
    {
        public long UserId { get; set; }
        public bool IsOwner { get; set; }
        public bool IsActive { get; set; } = true;

    }
}
