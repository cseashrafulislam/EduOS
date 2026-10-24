using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;

namespace EduOS.Core.Interfaces.IServices;

public interface ILoanRecoveryService
{
    Task<ApiResponse<LoanRecoveryRegisterDto>> GetRegisterAsync(
        bool openOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<LoanRecoveryHistoryDto>>> GetHistoryAsync(
        long loanId, CancellationToken ct = default);
    Task<ApiResponse<PostLoanRecoveryResultDto>> PostAsync(
        long loanId, PostLoanRecoveryRequestDto request, CancellationToken ct = default);
}
