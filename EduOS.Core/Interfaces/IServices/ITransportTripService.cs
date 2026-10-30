using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
namespace EduOS.Core.Interfaces.IServices;
public interface ITransportTripService
{
    Task<ApiResponse<TransportTripRowDto>> PlanAsync(PlanTransportTripRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<TransportTripRowDto>> DispatchAsync(Guid reference, DispatchTransportTripRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<TransportTripRowDto>> CompleteAsync(Guid reference, CompleteTransportTripRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportTripRowDto>>> GetAsync(DateOnly date, long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportTripRiderDto>>> GetRidersAsync(Guid reference, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<TransportTripAttendanceDto>> RecordAttendanceAsync(Guid reference, RecordTransportTripAttendanceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<TransportTripAttendanceDto>> CorrectAttendanceAsync(Guid reference, CorrectTransportTripAttendanceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<TransportTripAttendanceHistoryDto>>> GetAttendanceHistoryAsync(Guid reference, string studentCode, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportFamilyAttendanceDto>>> GetFamilyAttendanceAsync(DateOnly date, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportTripRowDto>>> GetDriverTripsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportTripRiderDto>>> GetDriverRidersAsync(Guid reference, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<TransportTripAttendanceDto>> RecordDriverAttendanceAsync(Guid reference, RecordTransportTripAttendanceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<string>> ExportRidersCsvAsync(Guid reference, CancellationToken ct = default);
}
