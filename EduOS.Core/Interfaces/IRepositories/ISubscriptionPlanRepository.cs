using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Small public plan catalog and tenant-eligible subscription offers.</summary>
public interface ISubscriptionPlanRepository : IGenericRepository<SubscriptionPlan>
{
    Task<List<SubscriptionPlan>> GetActivePublicPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetWithFeaturesAsync(long id, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetTrialPlanAsync(CancellationToken cancellationToken = default);
}
