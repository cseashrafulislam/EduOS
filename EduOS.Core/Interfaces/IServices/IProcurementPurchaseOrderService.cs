using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IProcurementPurchaseOrderService
{
    Task<ApiResponse<PagedResult<ProcurementPurchaseOrderDto>>> ListAsync(int page, int pageSize,
        long? campusId, string? state, string? search, CancellationToken ct = default);
    Task<ApiResponse<ProcurementPurchaseOrderDto>> SaveDraftAsync(
        long id, SaveProcurementPurchaseOrderDraftRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementPurchaseOrderDto>> GetAsync(long id, CancellationToken ct = default);
    Task<ApiResponse<ProcurementPurchaseOrderDto>> CreateAsync(
        CreateProcurementPurchaseOrderRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementPurchaseOrderDto>> TransitionAsync(long id, string action,
        ProcurementPurchaseOrderDecisionRequest request, CancellationToken ct = default);
}
