using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Tenant-scoped subscription lifecycle; platform-wide expiration checks must be explicitly privileged and paged.</summary>
public interface ITenantSubscriptionRepository : IGenericRepository<TenantSubscription>
{
    Task<TenantSubscription?> GetActiveByTenantAsync(long tenantId, CancellationToken cancellationToken = default);
    Task<TenantSubscription?> GetByIdForSystemAsync(long id, long tenantId, CancellationToken cancellationToken = default);
    Task<(List<TenantSubscription> Items, int TotalCount)> GetHistoryByTenantAsync(long tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<TenantSubscription> Items, int TotalCount)> GetExpiringSoonForPlatformAsync(DateTime utcCutoff, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<TenantSubscription> Items, int TotalCount)> GetExpiredForPlatformAsync(DateTime utcNow, int page, int pageSize, CancellationToken cancellationToken = default);
}
