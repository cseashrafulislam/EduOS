using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;

namespace EduOS.Core.Interfaces.IServices;

public interface IPayrollRunCancellationService
{
    Task<ApiResponse<CancelPayrollRunResultDto>> CancelAsync(Guid reference,
        CancelPayrollRunRequestDto request, CancellationToken ct = default);
}
