using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;

namespace EduOS.Core.Interfaces.IServices;

public interface IFacilityWorkflowService
{
    Task<ApiResponse<PagedResult<FacilityLocationOptionDto>>> LocationsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<FacilityRequestDto>> CreateAsync(CreateFacilityRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityRequestDto>>> ListAsync(long? campusId, string? state, int page, int pageSize, CancellationToken ct = default, bool overdueOnly = false);
    Task<ApiResponse<FacilityDashboardDto>> DashboardAsync(long? campusId, CancellationToken ct = default);
    Task<ApiResponse<FacilityRequestDto>> TransitionAsync(long id, string action, FacilityTransitionDto command, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityEventDto>>> HistoryAsync(long id, int page, int pageSize, CancellationToken ct = default);
}