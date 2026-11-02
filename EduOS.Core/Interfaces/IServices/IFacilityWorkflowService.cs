using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;

namespace EduOS.Core.Interfaces.IServices;

public interface IFacilityWorkflowService
{
    Task<ApiResponse<PagedResult<FacilityLocationOptionDto>>> LocationsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityVendorOptionDto>>> VendorsAsync(long campusId, string? search, int page, int pageSize, CancellationToken ct = default, long? workOrderId = null,
        DateTime? startUtc = null, DateTime? endUtc = null);
    Task<ApiResponse<PagedResult<FacilityTechnicianOptionDto>>> TechniciansAsync(long campusId, string? search, int page, int pageSize, CancellationToken ct = default, DateTime? startUtc = null, DateTime? endUtc = null, long? workOrderId = null);
    Task<ApiResponse<FacilityRequestDto>> CreateAsync(CreateFacilityRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityRequestDto>>> ListAsync(long? campusId, string? state, int page, int pageSize, CancellationToken ct = default, bool overdueOnly = false);
    Task<ApiResponse<IReadOnlyList<FacilityRequestDto>>> ExportSnapshotAsync(long? campusId, string? state, bool overdueOnly, CancellationToken ct = default);
    Task<ApiResponse<FacilityDashboardDto>> DashboardAsync(long? campusId, CancellationToken ct = default);
    Task<ApiResponse<FacilityRequestDto>> TransitionAsync(long id, string action, FacilityTransitionDto command, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityEventDto>>> HistoryAsync(long id, int page, int pageSize, CancellationToken ct = default);
}