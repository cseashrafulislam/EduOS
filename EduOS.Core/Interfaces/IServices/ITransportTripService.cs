using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
namespace EduOS.Core.Interfaces.IServices;
public interface ITransportTripService
{
    Task<ApiResponse<TransportTripRowDto>> PlanAsync(PlanTransportTripRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<TransportTripRowDto>> DispatchAsync(Guid reference, DispatchTransportTripRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportTripRowDto>>> GetAsync(DateOnly date, long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportTripRiderDto>>> GetRidersAsync(Guid reference, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<string>> ExportRidersCsvAsync(Guid reference, CancellationToken ct = default);
}
