namespace EduOS.Core.Entities.Base
{
    /// <summary>
    /// Marks an entity whose data belongs to exactly one tenant.
    /// Every implementation is protected by the DbContext tenant query filter.
    /// </summary>
    public interface ITenantScopedEntity
    {
        long TenantId { get; set; }
    }
}
