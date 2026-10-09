using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IServices;

public interface IOnboardingService
{
    Task<ApiResponse<OnboardingStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> AdvanceToStageAsync(OnboardingStage stage, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> CompleteStageAsync(CompleteOnboardingStageRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> CompleteOnboardingAsync(CancellationToken cancellationToken = default);
}
