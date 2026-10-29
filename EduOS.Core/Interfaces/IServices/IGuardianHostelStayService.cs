using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;

namespace EduOS.Core.Interfaces.IServices;

public interface IGuardianHostelStayService
{
    Task<ApiResponse<PagedResult<GuardianHostelStayRowDto>>> GetCurrentStaysAsync(
        int page, int pageSize, string? search, CancellationToken ct = default);
    Task<ApiResponse<string>> ExportCurrentPageCsvAsync(
        int page, string? search, CancellationToken ct = default);
}
