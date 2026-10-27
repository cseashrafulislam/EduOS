using EduOS.Core.Common;
using EduOS.Core.DTOs.Learners;

namespace EduOS.Core.Interfaces.IServices;

public interface ILearnerConsentService
{
    Task<ApiResponse<IReadOnlyList<LearnerIdentityConsentSummaryDto>>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<LearnerIdentityGrantSummaryDto>>> GetActiveGrantsAsync(
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResult<LearnerIdentityConsentHistoryDto>>> GetHistoryAsync(
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    Task<ApiResponse<LearnerConsentResolutionDto>> ResolveAsync(
        Guid requestReference,
        ResolveLearnerIdentityConsentRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<LearnerConsentResolutionDto>> RevokeAsync(
        Guid grantReference,
        CancellationToken cancellationToken = default);
}
