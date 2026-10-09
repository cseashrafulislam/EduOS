using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped vehicle, route, stop, driver and assignment administration.</summary>
public interface ITransportAdministrationService
{
    Task<ApiResponse<RouteDto>> SaveRouteAsync(Guid? routeReference, SaveRouteRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RouteStopDto>> SaveRouteStopAsync(long routeId, SaveRouteStopRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDto>> SaveVehicleAsync(Guid? vehicleReference, SaveVehicleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<RouteDto>>> GetRoutesAsync(long? campusId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<VehicleDto>>> GetVehiclesAsync(long? campusId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentTransportDto>>> GetAssignmentsAsync(Guid? routeReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportDriverDto>>> GetDriversAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportDriverDto>> SaveDriverAsync(long? driverId, SaveTransportDriverRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDriverAssignmentDto>> AssignDriverAsync(AssignVehicleDriverRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDriverAssignmentDto>> CloseDriverAssignmentAsync(long assignmentId, CloseTransportAssignmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RouteVehicleAssignmentDto>> AssignRouteVehicleAsync(AssignRouteVehicleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RouteVehicleAssignmentDto>> CloseRouteVehicleAssignmentAsync(long assignmentId, CloseTransportAssignmentRequestDto request, CancellationToken cancellationToken = default);
}
