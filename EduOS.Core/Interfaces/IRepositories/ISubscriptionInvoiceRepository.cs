using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Platform subscription invoices; number allocation belongs to INumberSeriesService.</summary>
public interface ISubscriptionInvoiceRepository : IGenericRepository<SubscriptionInvoice>
{
    Task<SubscriptionInvoice?> GetByIdForSystemAsync(long id, long tenantId, CancellationToken cancellationToken = default);
    Task<bool> HasFullyPaidInvoiceForSubscriptionAsync(long tenantId, long subscriptionId, CancellationToken cancellationToken = default);
    Task<SubscriptionInvoice?> GetByIdForPlatformAsync(long id, CancellationToken cancellationToken = default);
    Task<SubscriptionInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, long tenantId, CancellationToken cancellationToken = default);
    Task<(List<SubscriptionInvoice> Items, int TotalCount)> GetByTenantAsync(long tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<SubscriptionInvoice> Items, int TotalCount)> GetByStateAsync(long tenantId, InvoiceState state, int page, int pageSize, CancellationToken cancellationToken = default);
}
