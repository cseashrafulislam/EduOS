namespace EduOS.Core.Entities.Base
{
    public abstract class BaseTenantEntity : BaseEntity, ITenantScopedEntity
    {
        public long TenantId { get; set; }
        public virtual Tenants.Tenant? Tenant { get; set; }
    }
}
