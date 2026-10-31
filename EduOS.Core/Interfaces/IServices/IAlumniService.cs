using EduOS.Core.Common;
using EduOS.Core.DTOs.Alumni;

namespace EduOS.Core.Interfaces.IServices;

public interface IAlumniService
{
    Task<ApiResponse<IReadOnlyList<AlumniSelfDto>>> MineAsync(CancellationToken ct = default);
    Task<ApiResponse<AlumniSelfDto>> SaveConsentAsync(AlumniConsentRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlumniDirectoryItemDto>>> DirectoryAsync(AlumniDirectoryQueryDto query, CancellationToken ct = default);
}
