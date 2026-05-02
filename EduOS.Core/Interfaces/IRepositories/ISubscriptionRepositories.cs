using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ISubscriptionPlanRepository : IGenericRepository<SubscriptionPlan>
    {
        Task<List<SubscriptionPlan>> GetActivePublicPlansAsync(CancellationToken ct = default);
        Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default);
        Task<SubscriptionPlan?> GetWithFeaturesAsync(long id, CancellationToken ct = default);
        Task<SubscriptionPlan?> GetTrialPlanAsync(CancellationToken ct = default);
    }

    public interface ITenantSubscriptionRepository : IGenericRepository<TenantSubscription>
    {
        Task<TenantSubscription?> GetActiveByTenantAsync(long tenantId, CancellationToken ct = default);
        Task<List<TenantSubscription>> GetHistoryByTenantAsync(long tenantId, CancellationToken ct = default);
        Task<List<TenantSubscription>> GetExpiringSoonAsync(int daysAhead, CancellationToken ct = default);
        Task<List<TenantSubscription>> GetExpiredAsync(CancellationToken ct = default);
    }

    public interface ISubscriptionInvoiceRepository : IGenericRepository<SubscriptionInvoice>
    {
        Task<SubscriptionInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default);
        Task<List<SubscriptionInvoice>> GetByTenantAsync(long tenantId, CancellationToken ct = default);
        Task<List<SubscriptionInvoice>> GetUnpaidByTenantAsync(long tenantId, CancellationToken ct = default);
        Task<string> GenerateNextInvoiceNumberAsync(CancellationToken ct = default);
    }

    public interface ISubscriptionPaymentRepository : IGenericRepository<SubscriptionPayment>
    {
        Task<SubscriptionPayment?> GetByTransactionIdAsync(string transactionId, CancellationToken ct = default);
        Task<SubscriptionPayment?> GetByGatewayTransactionIdAsync(string gatewayTxnId, CancellationToken ct = default);
        Task<List<SubscriptionPayment>> GetByInvoiceAsync(long invoiceId, CancellationToken ct = default);
        Task<List<SubscriptionPayment>> GetPendingManualVerificationAsync(CancellationToken ct = default);
    }
}
