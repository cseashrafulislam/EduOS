using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Core.Interfaces.IServices;

public interface ITransportService
{
    Task<ApiResponse<PagedResult<TransportDriverReadinessRowDto>>> GetDriverReadinessAsync(long? campusId, int page, int pageSize, string? search, string? state, int withinDays, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportDriverReadinessCsvAsync(long? campusId, string? search, string? state, int withinDays, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportFleetInsightsDto>> GetFleetInsightsAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportVehicleDriverLinksCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportVehicleDriverLinkRowDto>>> GetVehicleDriverLinksAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDriverAssignmentDto>> AssignDriverAsync(AssignVehicleDriverRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<VehicleDriverAssignmentDto>> CloseDriverAssignmentAsync(long assignmentId, CloseTransportAssignmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportDriverDto>>> GetDriversAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportDriverLicenseComplianceRowDto>>> GetDriverLicenseComplianceAsync(int page, int pageSize, string? search, string? state, int withinDays, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportDriverLicenseComplianceCsvAsync(string? search, string? state, int withinDays, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportDriverDto>> SaveDriverAsync(long? driverId, SaveTransportDriverRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportCapacityReportRowDto>>> GetCapacityReportAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportCapacityCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportRouteVehicleLinksCsvAsync(long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportRouteVehicleLinkRowDto>>> GetRouteVehicleLinksAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<RouteVehicleAssignmentDto>> AssignRouteVehicleAsync(CreateTransportRouteVehicleLinkRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RouteVehicleAssignmentDto>> CloseRouteVehicleAssignmentAsync(long assignmentId, CloseTransportAssignmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportVehicleSetupRowDto>>> GetVehicleSetupPageAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportVehicleSetupRowDto>> CreateVehicleSetupAsync(CreateTransportVehicleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportVehicleSetupRowDto>> UpdateVehicleSetupAsync(Guid reference, UpdateTransportVehicleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<TransportCampusOptionDto>>> GetRouteSetupCampusesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportRouteSetupRowDto>>> GetRouteSetupPageAsync(long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportRouteSetupRowDto>> CreateRouteWithStopsAsync(CreateTransportRouteRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportRouteStopsEditorDto>> GetRouteStopsForEditAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportRouteStopsEditorDto>> AppendRouteStopAsync(Guid reference, AppendTransportRouteStopRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TransportRouteStopsEditorDto>> UpdateRouteStopsAsync(Guid reference, UpdateTransportRouteStopsRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<RouteDto>>> GetRoutesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<VehicleDto>>> GetVehiclesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportStudentOptionDto>>> GetEligibleStudentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportAssignmentHistoryRowDto>>> GetAssignmentHistoryAsync(long? campusId, int page, int pageSize, string? search, TransportAssignmentState? state, DateOnly? fromDate, DateOnly? toDate, Guid? routeReference, Guid? vehicleReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<string>> ExportAssignmentHistoryCsvAsync(long? campusId, string? search, TransportAssignmentState? state, DateOnly? fromDate, DateOnly? toDate, Guid? routeReference, Guid? vehicleReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportAssignmentRowDto>>> GetActiveAssignmentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto?>> GetMyAssignmentAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto>> AssignAsync(AssignStudentTransportRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto>> CloseAsync(Guid reference, CloseStudentTransportRequestDto request, CancellationToken cancellationToken = default);
}
