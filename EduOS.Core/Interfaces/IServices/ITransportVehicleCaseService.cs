using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Core.Interfaces.IServices;

public interface ITransportVehicleCaseService
{
    Task<ApiResponse<PagedResult<TransportVehicleCaseRowDto>>> GetAsync(
        long? campusId, string? state, string? search, int page, int pageSize,
        CancellationToken ct = default);
    Task<ApiResponse<TransportVehicleCaseRowDto>> CreateAsync(
        CreateTransportVehicleCaseRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<TransportVehicleCaseRowDto>> ResolveAsync(
        Guid reference, ResolveTransportVehicleCaseRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<string>> ExportCsvAsync(
        long? campusId, string? state, string? search, CancellationToken ct = default);
}
