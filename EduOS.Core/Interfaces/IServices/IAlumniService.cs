using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;

namespace EduOS.Core.Interfaces.IServices;

public interface IAlumniService
{
    Task<ApiResponse<IReadOnlyList<AlumniEventCampusDto>>> EventCampusesAsync(CancellationToken ct = default);
    Task<ApiResponse<AlumniEventItemDto>> CreateEventAsync(AlumniEventDraftDto request, CancellationToken ct = default);
    Task<ApiResponse<AlumniEventItemDto>> ChangeEventStatusAsync(Guid reference, AlumniEventActionDto request, bool publish, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlumniEventItemDto>>> EventsAsync(AlumniEventQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlumniEventAttendanceItemDto>>> AttendanceAsync(Guid eventReference, AlumniEventAttendanceQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<AlumniEventAttendanceItemDto>> CheckInAsync(Guid eventReference, AlumniEventCheckInDto request, CancellationToken ct = default);
    Task<ApiResponse<AlumniEventItemDto>> RsvpAsync(AlumniEventRsvpDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<AlumniSelfDto>>> MineAsync(CancellationToken ct = default);
    Task<ApiResponse<AlumniSelfDto>> SaveConsentAsync(AlumniConsentRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlumniDirectoryItemDto>>> DirectoryAsync(AlumniDirectoryQueryDto query, CancellationToken ct = default);
}
