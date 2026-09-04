using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Entities.Base
{
    public abstract class BaseTenantEntity : BaseEntity
    {
        public long TenantId { get; set; }
        public virtual Tenant? Tenant { get; set; }
    }
}
