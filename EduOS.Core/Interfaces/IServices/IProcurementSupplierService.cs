using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
namespace EduOS.Core.Interfaces.IServices;
public interface IProcurementSupplierService
{
    Task<ApiResponse<PagedResult<ProcurementSupplierDto>>> ListAsync(int page,int pageSize,long? campusId,string? state,string? search,CancellationToken ct=default);
    Task<ApiResponse<ProcurementSupplierDto>> GetAsync(long id,CancellationToken ct=default);
    Task<ApiResponse<ProcurementSupplierDto>> SaveAsync(long? id,SaveProcurementSupplierRequest request,CancellationToken ct=default);
    Task<ApiResponse<ProcurementSupplierDto>> TransitionAsync(long id,string action,ProcurementSupplierDecisionRequest request,CancellationToken ct=default);
}
