using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Core.Interfaces.IServices;

public interface ITransportAdministrationService
{
    Task<ApiResponse<RouteDto>> SaveRouteAsync(Guid? routeReference, SaveRouteRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDto>> SaveVehicleAsync(Guid? vehicleReference, SaveVehicleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RouteDto>>> GetRoutesAsync(long? campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<VehicleDto>>> GetVehiclesAsync(long? campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentTransportDto>>> GetAssignmentsAsync(Guid? routeReference, int page, int pageSize, CancellationToken cancellationToken = default);
}
