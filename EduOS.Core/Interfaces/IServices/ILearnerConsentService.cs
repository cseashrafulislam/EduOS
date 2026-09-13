using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;

namespace EduOS.Core.Interfaces.IServices;

public interface ILearnerConsentService
{
    Task<ApiResponse<IReadOnlyList<LearnerConsentRequestDto>>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<LearnerDataGrantDto>>> GetActiveGrantsAsync(
        CancellationToken cancellationToken = default);

    Task<ApiResponse<LearnerConsentResolutionDto>> ResolveAsync(
        Guid requestReference,
        ResolveLearnerConsentRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<LearnerConsentResolutionDto>> RevokeAsync(
        Guid grantReference,
        CancellationToken cancellationToken = default);
}
