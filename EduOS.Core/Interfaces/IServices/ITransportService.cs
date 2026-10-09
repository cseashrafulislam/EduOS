using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Core.Interfaces.IServices;

public interface ITransportService
{
    Task<ApiResponse<IReadOnlyList<TransportRouteDto>>> GetRoutesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<TransportVehicleDto>>> GetVehiclesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportStudentOptionDto>>> GetEligibleStudentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TransportAssignmentRowDto>>> GetActiveAssignmentsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto?>> GetMyAssignmentAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto>> AssignAsync(AssignTransportDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransportDto>> CloseAsync(Guid reference, CloseTransportDto request, CancellationToken cancellationToken = default);
}
