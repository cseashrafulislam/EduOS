using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;

namespace EduOS.Core.Interfaces.IServices;

public interface IRecruitmentService
{
    Task<ApiResponse<PagedResult<RecruitmentCandidateDto>>> ListAsync(
        string? search, long? campusId, RecruitmentStage? stage, int page, int pageSize,
        CancellationToken ct = default);
    Task<ApiResponse<RecruitmentCandidateDto>> CreateAsync(
        RecruitmentCreateRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<RecruitmentCandidateDto>> TransitionAsync(
        Guid reference, RecruitmentTransitionRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<RecruitmentStageHistoryDto>>> HistoryAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default);
}
