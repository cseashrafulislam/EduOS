using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IFeeReceivablesAgingService
{
    Task<ApiResponse<FeeReceivablesAgingReportDto>> GetReportAsync(
        FeeReceivablesAgingQueryDto request, CancellationToken cancellationToken = default);
}
