using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
namespace EduOS.Core.Interfaces.IServices;
public interface IPayrollJournalPostingService
{
    Task<ApiResponse<PayrollPostingRegisterDto>> GetRegisterAsync(bool unpostedOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PostPayrollJournalResultDto>> PostAsync(Guid reference, PostPayrollJournalRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PayrollReversalResultDto>> RequestReversalAsync(Guid reference, RequestPayrollReversalDto request, CancellationToken ct = default);
    Task<ApiResponse<PayrollReversalResultDto>> ReviewReversalAsync(Guid reference, ReviewPayrollReversalDto request, CancellationToken ct = default);
    Task<ApiResponse<PayrollReversalResultDto>> PostReversalAsync(Guid reference, PostPayrollReversalDto request, CancellationToken ct = default);
}
