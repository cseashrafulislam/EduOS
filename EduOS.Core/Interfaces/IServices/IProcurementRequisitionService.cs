using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IProcurementRequisitionService
{
    Task<ApiResponse<PagedResult<ProcurementRequisitionDto>>> ListAsync(int page, int pageSize, long? campusId, string? state, string? search, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ProcurementRequisitionDto>>> PendingApprovalsAsync(int page, int pageSize, long? campusId, string? search, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ProcurementRequisitionHistoryDto>>> HistoryAsync(long id, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<ProcurementRequisitionDto>> GetAsync(long id, CancellationToken ct = default);
    Task<ApiResponse<ProcurementRequisitionDto>> SaveAsync(long? id, SaveProcurementRequisitionRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementRequisitionDto>> TransitionAsync(long id, string action, ProcurementDecisionRequest request, CancellationToken ct = default);
}
