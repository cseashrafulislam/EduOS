using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IFeeReceivablesAgingService
{
    Task<ApiResponse<FeeDueCalendarReportDto>> GetDueCalendarAsync(
        FeeDueCalendarQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<DailyCashCollectionReportDto>> GetDailyCashCollectionsAsync(
        DailyCashCollectionQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeePaymentReconciliationRegisterDto>> GetPaymentReconciliationAsync(
        long? campusId, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeReconciliationRegisterDto>> GetReconciliationAsync(
        long? campusId, string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeReceivablesAgingReportDto>> GetReportAsync(
        FeeReceivablesAgingQueryDto request, CancellationToken cancellationToken = default);
}
