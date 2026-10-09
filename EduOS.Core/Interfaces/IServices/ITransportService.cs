using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Core.Interfaces.IServices;

public interface ITransportService
{
    Task<ApiResponse<IReadOnlyList<RouteDto>>> GetRoutesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<VehicleDto>>> GetVehiclesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportStudentOptionDto>>> GetEligibleStudentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportAssignmentRowDto>>> GetActiveAssignmentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto?>> GetMyAssignmentAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto>> AssignAsync(AssignStudentTransportRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto>> CloseAsync(Guid reference, CloseStudentTransportRequestDto request, CancellationToken cancellationToken = default);
}
