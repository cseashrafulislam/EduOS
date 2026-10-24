using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;
namespace EduOS.Core.Interfaces.IServices;
public interface IPayrollJournalPostingService
{
    Task<ApiResponse<PayrollPostingRegisterDto>> GetRegisterAsync(bool unpostedOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PostPayrollJournalResultDto>> PostAsync(Guid reference, PostPayrollJournalRequestDto request, CancellationToken ct = default);
}
