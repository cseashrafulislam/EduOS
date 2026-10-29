using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Core.Interfaces.IServices;

public interface ITransportFamilyScheduleService
{
    Task<ApiResponse<PagedResult<TransportFamilyScheduleDto>>> GetAsync(
        DateOnly? operatingDate, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<string>> ExportCsvAsync(DateOnly? operatingDate, CancellationToken ct = default);
}
