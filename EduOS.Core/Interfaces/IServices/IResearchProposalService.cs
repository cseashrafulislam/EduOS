using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;

namespace EduOS.Core.Interfaces.IServices;

public interface IResearchProposalService
{
    Task<ApiResponse<List<ResearchCampusDto>>> CampusesAsync(CancellationToken ct = default);
    Task<ApiResponse<List<ResearchProgramDto>>> ProgramsAsync(long campusId, CancellationToken ct = default);
    Task<ApiResponse<List<ResearchReviewerDto>>> ReviewersAsync(long campusId, string? search, CancellationToken ct = default);
    Task<ApiResponse<ResearchProposalDto>> CreateAsync(ResearchProposalCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<ResearchProposalDto>> CommandAsync(Guid reference, string action, ResearchProposalCommandDto input, CancellationToken ct = default);
    Task<ApiResponse<ResearchProposalDto>> DetailAsync(Guid reference, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ResearchProposalDto>>> ListAsync(long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ResearchProposalHistoryDto>>> HistoryAsync(Guid reference, int page, int pageSize, CancellationToken ct = default);
}
