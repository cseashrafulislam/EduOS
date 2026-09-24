using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;

namespace EduOS.Core.Interfaces.IServices;

public interface IHostelService
{
    Task<ApiResponse<IReadOnlyList<HostelRoomDto>>> GetRoomsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelDto?>> GetMyAllocationAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelDto>> AllocateAsync(AllocateHostelDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentHostelDto>> CloseAsync(long id, CloseHostelAllocationDto request, CancellationToken cancellationToken = default);
}
