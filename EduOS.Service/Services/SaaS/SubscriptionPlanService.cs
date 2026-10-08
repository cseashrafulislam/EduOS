using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS
{
    public class SubscriptionPlanService : ISubscriptionPlanService
    {
        private readonly ISubscriptionPlanRepository _planRepository;
        private readonly IGenericRepository<PlanFeature> _planFeatureRepository;
        private readonly IGenericRepository<Feature> _featureRepository;
        private readonly IMapper _mapper;
        private readonly ILogger<SubscriptionPlanService> _logger;

        public SubscriptionPlanService(
            ISubscriptionPlanRepository planRepository,
            IGenericRepository<PlanFeature> planFeatureRepository,
            IGenericRepository<Feature> featureRepository,
            IMapper mapper,
            ILogger<SubscriptionPlanService> logger)
        {
            _planRepository = planRepository;
            _planFeatureRepository = planFeatureRepository;
            _featureRepository = featureRepository;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ApiResponse<List<SubscriptionPlanDto>>> GetPublicPlansAsync()
        {
            try
            {
                var plans = await _planRepository.GetActivePublicPlansAsync();
                var dtos = await MapPlansWithFeaturesAsync(plans);
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

                if (plan == null || !plan.IsActive || !plan.IsPublic)
                    return ApiResponse<SubscriptionPlanDto>.ErrorResponse("Plan not found", 404);

                var dto = (await MapPlansWithFeaturesAsync(new List<SubscriptionPlan> { plan }))[0];
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

                if (plan == null || !plan.IsActive || !plan.IsPublic)
                    return ApiResponse<SubscriptionPlanDto>.ErrorResponse("Plan not found", 404);

                var dto = (await MapPlansWithFeaturesAsync(new List<SubscriptionPlan> { plan }))[0];
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
                var planDtos = await MapPlansWithFeaturesAsync(plans);
                var allFeatures = planDtos.SelectMany(p => p.Features)
                    .GroupBy(f => f.FeatureId)
                    .Select(g => g.First())
                    .OrderBy(f => f.FeatureName)
                    .ToList();
                var categories = allFeatures.Count == 0 ? new List<FeatureCategoryDto>() :
                    new List<FeatureCategoryDto>
                    {
                        new FeatureCategoryDto
                        {
                            Category = "General",
                            Features = allFeatures.Select(feature => new FeatureItemDto
                            {
                                FeatureId = feature.FeatureId,
                                Code = feature.FeatureCode,
                                Name = feature.FeatureName,
                                PlanAvailability = planDtos.ToDictionary(
                                    p => p.Id,
                                    p => p.Features.Any(f => f.FeatureId == feature.FeatureId && f.IsEnabled))
                            }).ToList()
                        }
                    };

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
        private async Task<List<SubscriptionPlanDto>> MapPlansWithFeaturesAsync(List<SubscriptionPlan> plans)
        {
            var dtos = _mapper.Map<List<SubscriptionPlanDto>>(plans);
            if (dtos.Count == 0) return dtos;

            var ids = plans.Select(p => p.Id).Distinct().ToArray();
            var assignments = await (
                from assignment in _planFeatureRepository.GetQueryable().AsNoTracking()
                join feature in _featureRepository.GetQueryable().AsNoTracking()
                    on assignment.FeatureId equals feature.Id
                where ids.Contains(assignment.SubscriptionPlanId) && feature.IsActive
                select new
                {
                    assignment.SubscriptionPlanId,
                    assignment.FeatureId,
                    assignment.IsEnabled,
                    assignment.LimitValue,
                    feature.Code,
                    feature.Name
                }).ToListAsync();

            var lookup = assignments.GroupBy(x => x.SubscriptionPlanId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Name)
                    .Select(x => new PlanFeatureDto
                    {
                        FeatureId = x.FeatureId,
                        FeatureCode = x.Code,
                        FeatureName = x.Name,
                        IsEnabled = x.IsEnabled,
                        LimitValue = x.LimitValue
                    }).ToList());
            foreach (var dto in dtos)
                dto.Features = lookup.TryGetValue(dto.Id, out var features) ? features : new List<PlanFeatureDto>();
            return dtos;
        }
    }
}
