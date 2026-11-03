using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;

namespace EduOS.Core.Interfaces.IServices;
public interface IFacilityVisitorService
{
    Task<ApiResponse<PagedResult<FacilityVisitorDto>>> ListAsync(long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityVisitorHostDto>>> HostsAsync(long campusId, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<FacilityVisitorOccupancyDto>> OccupancyAsync(long campusId, CancellationToken ct = default);
    Task<ApiResponse<FacilityVisitorDto>> CreateAsync(CreateFacilityVisitorDto input, CancellationToken ct = default);
    Task<ApiResponse<FacilityVisitorDto>> TransitionAsync(long id, string action, FacilityVisitorTransitionDto command, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<FacilityVisitorExportRowDto>>> ExportSnapshotAsync(long campusId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityVisitorEventDto>>> HistoryAsync(long id, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<FacilityVisitorPolicyDto>>> PolicyChangesAsync(long campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<FacilityVisitorPolicyDto>> RequestPolicyChangeAsync(FacilityVisitorPolicyRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<FacilityVisitorPolicyDto>> DecidePolicyChangeAsync(long id, string decision, FacilityVisitorPolicyDecisionDto request, CancellationToken ct = default);
}
