using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface ISubscriptionInvoiceService
{
    Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetMyInvoicesAsync();
    Task<ApiResponse<SubscriptionInvoiceDto>> GetByIdAsync(long invoiceId);
    Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetUnpaidAsync();
}
