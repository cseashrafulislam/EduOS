using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS
{
    public class SubscriptionPlanService : ISubscriptionPlanService
    {
        private readonly ISubscriptionPlanRepository _planRepository;
        private readonly IMapper _mapper;
        private readonly ILogger<SubscriptionPlanService> _logger;

        public SubscriptionPlanService(
            ISubscriptionPlanRepository planRepository,
            IMapper mapper,
            ILogger<SubscriptionPlanService> logger)
        {
            _planRepository = planRepository;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ApiResponse<List<SubscriptionPlanDto>>> GetPublicPlansAsync()
        {
            try
            {
                var plans = await _planRepository.GetActivePublicPlansAsync();
                var dtos = _mapper.Map<List<SubscriptionPlanDto>>(plans);
                return ApiResponse<List<SubscriptionPlanDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch public plans");
                return ApiResponse<List<SubscriptionPlanDto>>.ErrorResponse("Failed to load plans", 500);
            }
        }

        public async Task<ApiResponse<SubscriptionPlanDto>> GetByIdAsync(long id)
        {
            try
            {
                var plan = await _planRepository.GetWithFeaturesAsync(id);

                if (plan == null || !plan.IsActive)
                    return ApiResponse<SubscriptionPlanDto>.ErrorResponse("Plan not found", 404);

                var dto = _mapper.Map<SubscriptionPlanDto>(plan);
                return ApiResponse<SubscriptionPlanDto>.SuccessResponse(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch plan {Id}", id);
                return ApiResponse<SubscriptionPlanDto>.ErrorResponse("Failed to load plan", 500);
            }
        }

        public async Task<ApiResponse<SubscriptionPlanDto>> GetByCodeAsync(string code)
        {
            try
            {
                var plan = await _planRepository.GetByCodeAsync(code);

                if (plan == null || !plan.IsActive)
                    return ApiResponse<SubscriptionPlanDto>.ErrorResponse("Plan not found", 404);

                var dto = _mapper.Map<SubscriptionPlanDto>(plan);
                return ApiResponse<SubscriptionPlanDto>.SuccessResponse(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch plan by code {Code}", code);
                return ApiResponse<SubscriptionPlanDto>.ErrorResponse("Failed to load plan", 500);
            }
        }

        public async Task<ApiResponse<PlanComparisonDto>> GetPlanComparisonAsync()
        {
            try
            {
                var plans = await _planRepository.GetActivePublicPlansAsync();
                var planDtos = _mapper.Map<List<SubscriptionPlanDto>>(plans);

                // Build feature category groups for comparison table
                var allFeatures = plans
                    .SelectMany(p => p.PlanFeatures)
                    .Where(pf => pf.Feature != null)
                    .Select(pf => pf.Feature!)
                    .DistinctBy(f => f.Id)
                    .ToList();

                var categories = allFeatures
                    .GroupBy(f => f.Category ?? "General")
                    .OrderBy(g => g.Min(f => f.DisplayOrder))
                    .Select(grp => new FeatureCategoryDto
                    {
                        Category = grp.Key,
                        Features = grp
                            .OrderBy(f => f.DisplayOrder)
                            .Select(f => new FeatureItemDto
                            {
                                FeatureId = f.Id,
                                Name = f.Name,
                                Code = f.Code,
                                PlanAvailability = plans.ToDictionary(
                                    p => p.Id,
                                    p => p.PlanFeatures.Any(pf => pf.FeatureId == f.Id && pf.IsEnabled)
                                )
                            })
                            .ToList()
                    })
                    .ToList();

                return ApiResponse<PlanComparisonDto>.SuccessResponse(new PlanComparisonDto
                {
                    Plans = planDtos,
                    FeatureCategories = categories
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build plan comparison");
                return ApiResponse<PlanComparisonDto>.ErrorResponse("Failed to load comparison", 500);
            }
        }
    }
}
