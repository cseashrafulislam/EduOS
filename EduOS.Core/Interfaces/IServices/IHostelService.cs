using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;

namespace EduOS.Core.Interfaces.IServices;

public interface IHostelService
{
    Task<ApiResponse<PagedResult<HostelCapacityRowDto>>> GetCapacityAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelBedInventoryDto>>> GetBedInventoryAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelBedInventoryDto>> SetBedStateAsync(long bedId, SetHostelBedStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<HostelRoomDto>>> GetRoomsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelStudentOptionDto>>> GetEligibleStudentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetAvailableBedsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HostelAllocationRowDto>>> GetActiveAllocationsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto?>> GetMyAllocationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> AllocateAsync(AllocateStudentHostelRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelAllocationDto>> CloseAsync(long id, CloseStudentHostelAllocationRequestDto request, CancellationToken cancellationToken = default);
}
