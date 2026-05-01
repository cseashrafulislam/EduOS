namespace EduOS.Core.Entities.Base
{
    public abstract class BaseTenantEntity : BaseEntity
    {
        public int TenantId { get; set; }
        public virtual Tenants.Tenant? Tenant { get; set; }
    }
}
