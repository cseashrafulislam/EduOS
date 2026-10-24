using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;

namespace EduOS.Core.Interfaces.IServices;

public interface ILoanDisbursementService
{
    Task<ApiResponse<LoanDisbursementRegisterDto>> GetRegisterAsync(
        bool pendingOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PostLoanDisbursementResultDto>> PostAsync(
        long loanId, PostLoanDisbursementRequestDto request, CancellationToken ct = default);
}
