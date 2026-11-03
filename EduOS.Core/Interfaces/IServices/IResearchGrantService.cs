using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;

namespace EduOS.Core.Interfaces.IServices;

public interface IResearchGrantService
{
    Task<ApiResponse<List<ResearchGrantSchemeDto>>> SchemesAsync(CancellationToken ct = default);
    Task<ApiResponse<ResearchGrantSchemeDto>> CreateSchemeAsync(ResearchGrantSchemeCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<ResearchGrantDto>> CreateAsync(ResearchGrantCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<ResearchGrantDto>> CommandAsync(Guid reference, string action, ResearchGrantCommandDto input, CancellationToken ct = default);
    Task<ApiResponse<ResearchGrantDto>> DetailAsync(Guid reference, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ResearchGrantDto>>> ListAsync(long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<List<ResearchGrantMilestoneDto>>> MilestonesAsync(Guid reference, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ResearchGrantHistoryDto>>> HistoryAsync(Guid reference, int page, int pageSize, CancellationToken ct = default);
}
