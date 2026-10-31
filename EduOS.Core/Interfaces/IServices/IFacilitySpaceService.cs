using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;

namespace EduOS.Core.Interfaces.IServices;

public interface IFacilitySpaceService
{
    Task<ApiResponse<PagedResult<FacilitySpaceDto>>> ListAsync(
        long? campusId, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<FacilitySpaceDto>> SaveAsync(
        long? id, SaveFacilitySpaceRequestDto request, CancellationToken ct = default);
}
