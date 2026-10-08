using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;

namespace EduOS.Core.Interfaces.IServices;

public interface IHostelAdministrationService
{
    Task<ApiResponse<IReadOnlyList<HostelDto>>> GetHostelsAsync(long? campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelDto>> SaveHostelAsync(Guid? hostelReference, SaveHostelRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelRoomDto>> SaveRoomAsync(long? roomId, SaveHostelRoomRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelBedDto>> SaveBedAsync(long? bedId, SaveHostelBedRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<HostelBedDto>>> GetBedsAsync(long roomId, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentHostelAllocationDto>>> GetAllocationsAsync(long hostelId, int page, int pageSize, CancellationToken cancellationToken = default);
}
