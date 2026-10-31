using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IProcurementSupplierAcknowledgementService
{
    Task<ApiResponse<PagedResult<ProcurementSupplierAcknowledgementDto>>> ListAsync(
        int page, int pageSize, long? campusId, string? state, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierAcknowledgementDto>> GetAsync(long id, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierAcknowledgementDto>> SaveAsync(
        long? id, SaveProcurementSupplierAcknowledgementRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierAcknowledgementDto>> TransitionAsync(
        long id, string action, ProcurementSupplierAcknowledgementDecisionRequest request, CancellationToken ct = default);
}
