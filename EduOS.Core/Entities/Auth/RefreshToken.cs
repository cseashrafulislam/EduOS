using EduOS.Core.Entities.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Core.Entities.Auth
{
    public class RefreshToken : BaseTenantEntity
    {
        public long userId { get; set; } 
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; }
        public bool IsRevoked { get; set; }
        public string IpAddress { get; set; } = string.Empty;
    }
}
