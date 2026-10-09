using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;

namespace EduOS.Core.Interfaces.IServices;

public interface IOnboardingService
    {
        Task<ApiResponse<OnboardingStatusDto>> GetStatusAsync();
        Task<ApiResponse<bool>> AdvanceToStepAsync(EduOS.Core.Enums.OnboardingStep step);
        Task<ApiResponse<bool>> CompleteStepAsync(CompleteStepDto dto);
        Task<ApiResponse<bool>> CompleteOnboardingAsync();
    }
