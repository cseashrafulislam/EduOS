using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

public interface ILibraryLiabilityService
{
    Task<ApiResponse<PagedResult<LibraryPostingOptionDto>>> PostingOptionsAsync(string type,
        string? search, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<LibraryIncidentDto>>> IncidentsAsync(string? search, long? campusId,
        int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<LibraryLiabilityDto>>> RegisterAsync(string? status, long? campusId,
        int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<LibraryLiabilityDto>> AssessAsync(AssessLibraryLiabilityRequestDto request,
        CancellationToken ct = default);
    Task<ApiResponse<LibraryLiabilityDto>> ReviewAsync(Guid reference, ReviewLibraryLiabilityRequestDto request,
        CancellationToken ct = default);
}
