using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IServices;

public interface ISubscriptionInvoiceService
{
    Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetMyInvoicesAsync();
    Task<ApiResponse<PagedResult<SubscriptionInvoiceDto>>> GetPageAsync(int page, int pageSize, InvoiceState? state = null, CancellationToken ct = default);
    Task<ApiResponse<SubscriptionInvoiceDto>> GetByIdAsync(long invoiceId);
    Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetUnpaidAsync();
}
