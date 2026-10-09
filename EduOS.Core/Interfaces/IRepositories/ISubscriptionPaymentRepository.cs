using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Platform payment reconciliation must be authorized independently; standard queries remain tenant-scoped and paged.</summary>
public interface ISubscriptionPaymentRepository : IGenericRepository<SubscriptionPayment>
{
    Task<SubscriptionPayment?> GetByIdForPlatformAsync(long id, CancellationToken cancellationToken = default);
    Task<SubscriptionPayment?> GetByTransactionIdForCallbackAsync(string transactionId, CancellationToken cancellationToken = default);
    Task<SubscriptionPayment?> GetByGatewayTransactionIdAsync(string gatewayTransactionId, CancellationToken cancellationToken = default);
    Task<(List<SubscriptionPayment> Items, int TotalCount)> GetByInvoiceAsync(long invoiceId, long tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<SubscriptionPayment> Items, int TotalCount)> GetPendingManualVerificationForPlatformAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
