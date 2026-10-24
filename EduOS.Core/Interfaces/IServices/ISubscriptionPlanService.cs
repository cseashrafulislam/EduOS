using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices
{
    public interface ISubscriptionPlanService
    {
        /// <summary>
        /// Get all publicly visible plans for pricing page (no auth)
        /// </summary>
        Task<ApiResponse<List<SubscriptionPlanDto>>> GetPublicPlansAsync();

        /// <summary>
        /// Get single plan with features
        /// </summary>
        Task<ApiResponse<SubscriptionPlanDto>> GetByIdAsync(long id);

        /// <summary>
        /// Get plan by code (TRIAL, BASIC, PRO, ENTERPRISE)
        /// </summary>
        Task<ApiResponse<SubscriptionPlanDto>> GetByCodeAsync(string code);

        /// <summary>
        /// Side-by-side comparison view for pricing page
        /// </summary>
        Task<ApiResponse<PlanComparisonDto>> GetPlanComparisonAsync();

        Task<ApiResponse<SubscriptionPlanConfigurationDto>> GetPlanConfigurationAsync(long id, CancellationToken ct = default);
        Task<ApiResponse<SubscriptionPlanConfigurationDto>> SavePlanConfigurationAsync(long id, SaveSubscriptionPlanConfigurationRequestDto request, CancellationToken ct = default);

        Task<ApiResponse<IReadOnlyList<SubscriptionPlanAvailabilityDto>>> GetAdminPlanAvailabilityAsync(CancellationToken ct = default);
        Task<ApiResponse<SubscriptionPlanAvailabilityDto>> ChangePlanAvailabilityAsync(long id, ChangeSubscriptionPlanAvailabilityRequestDto request, CancellationToken ct = default);
    }
}
