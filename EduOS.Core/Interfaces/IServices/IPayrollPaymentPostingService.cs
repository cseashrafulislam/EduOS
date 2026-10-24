using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
namespace EduOS.Core.Interfaces.IServices;

public interface IPayrollPaymentPostingService
{
    Task<ApiResponse<PayrollPaymentPostingRegisterDto>> GetRegisterAsync(
        bool pendingOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PostPayrollPaymentJournalResultDto>> PostAsync(
        Guid paymentReference, PostPayrollPaymentJournalRequestDto request, CancellationToken ct = default);
}
