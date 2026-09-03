using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants
{
    public class OnboardingService : IOnboardingService
    {
        private readonly IGenericRepository<Tenant> _tenantRepo;
        private readonly IGenericRepository<Campus> _campusRepo;
        private readonly IGenericRepository<AcademicYear> _academicYearRepo;
        private readonly ITenantSubscriptionRepository _subscriptionRepo;
        private readonly ITenantModuleService _tenantModuleService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IMemoryCache _cache;
        private readonly ILogger<OnboardingService> _logger;

        public OnboardingService(
            IGenericRepository<Tenant> tenantRepo,
            IGenericRepository<Campus> campusRepo,
            IGenericRepository<AcademicYear> academicYearRepo,
            ITenantSubscriptionRepository subscriptionRepo,
            ITenantModuleService tenantModuleService,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IMemoryCache cache,
            ILogger<OnboardingService> logger)
        {
            _tenantRepo = tenantRepo;
            _campusRepo = campusRepo;
            _academicYearRepo = academicYearRepo;
            _subscriptionRepo = subscriptionRepo;
            _tenantModuleService = tenantModuleService;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _cache = cache;
            _logger = logger;
        }

        // ============================================================
        // GET STATUS
        // ============================================================
        public async Task<ApiResponse<OnboardingStatusDto>> GetStatusAsync()
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<OnboardingStatusDto>.ErrorResponse("Tenant not found", 404);

                var steps = BuildStepStatusList(tenant);
                var completed = steps.Count(s => s.IsCompleted);
                var total = steps.Count;

                var current = steps.FirstOrDefault(s => s.IsCurrent);

                var status = new OnboardingStatusDto
                {
                    TenantId = tenant.Id,
                    CurrentStep = tenant.OnboardingStep,
                    IsComplete = tenant.IsOnboardingComplete,
                    CompletedAt = tenant.OnboardingCompletedAt,
                    Steps = steps,
                    TotalSteps = total,
                    CompletedSteps = completed,
                    ProgressPercentage = total > 0 ? (int)Math.Round(completed * 100.0 / total) : 0,
                    NextStepCode = current?.Code,
                    NextStepName = current?.Name,
                    NextStepUrl = current?.Url
                };

                return ApiResponse<OnboardingStatusDto>.SuccessResponse(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load onboarding status");
                return ApiResponse<OnboardingStatusDto>.ErrorResponse("Failed to load status", 500);
            }
        }

        // ============================================================
        // ADVANCE TO SPECIFIC STEP
        // ============================================================
        public async Task<ApiResponse<bool>> AdvanceToStepAsync(OnboardingStep step)
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                if (tenant.IsOnboardingComplete)
                    return ApiResponse<bool>.ErrorResponse("Onboarding already completed", 400);

                var currentOrder = GetStepOrder(tenant.OnboardingStep);
                var targetOrder = GetStepOrder(step);
                if (currentOrder == 0 || targetOrder == 0)
                    return ApiResponse<bool>.ErrorResponse("Invalid onboarding step", 400);

                // This method may resume the current step or move exactly one
                // step forward. It cannot be used to bypass prerequisites.
                if (targetOrder < currentOrder)
                {
                    return ApiResponse<bool>.ErrorResponse(
                        "Cannot move backwards in onboarding", 400);
                }

                if (targetOrder > currentOrder + 1)
                    return ApiResponse<bool>.ErrorResponse("Cannot skip onboarding steps", 409);

                if (targetOrder == currentOrder + 1)
                {
                    var validation = await ValidateStepCompletionAsync(
                        tenant,
                        tenant.OnboardingStep);
                    if (!validation.Success) return validation;
                }

                tenant.OnboardingStep = step;
                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();
                ClearOnboardingCache(tenant.Id);

                return ApiResponse<bool>.SuccessResponse(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to advance step");
                return ApiResponse<bool>.ErrorResponse("Failed to advance step", 500);
            }
        }

        // ============================================================
        // COMPLETE STEP - mark current step done, advance to next
        // ============================================================
        public async Task<ApiResponse<bool>> CompleteStepAsync(CompleteStepDto dto)
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                if (tenant.IsOnboardingComplete)
                    return ApiResponse<bool>.ErrorResponse("Onboarding already completed", 400);

                if (GetStepOrder(dto.Step) == 0)
                    return ApiResponse<bool>.ErrorResponse("Invalid onboarding step", 400);

                if (dto.Step != tenant.OnboardingStep)
                {
                    return ApiResponse<bool>.ErrorResponse(
                        "This onboarding step is not currently active. Reload and continue from the current step.",
                        409);
                }

                if (dto.Skipped && !IsSkippable(dto.Step))
                    return ApiResponse<bool>.ErrorResponse("This onboarding step cannot be skipped", 409);

                // Validate step prerequisites
                var validation = await ValidateStepCompletionAsync(tenant, dto.Step);
                if (!validation.Success)
                    return validation;

                // Advance to next step
                var nextStep = GetNextStep(dto.Step);
                tenant.OnboardingStep = nextStep;

                if (nextStep == OnboardingStep.Completed)
                {
                    tenant.IsOnboardingComplete = true;
                    tenant.OnboardingCompletedAt = DateTime.UtcNow;
                    if (tenant.Status == TenantStatus.Onboarding)
                        tenant.Status = TenantStatus.Active;
                }

                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();
                ClearOnboardingCache(tenant.Id);

                _logger.LogInformation("Tenant {Id} completed step {Step}, now on {Next}",
                    tenant.Id, dto.Step, nextStep);

                return ApiResponse<bool>.SuccessResponse(true,
                    nextStep == OnboardingStep.Completed
                        ? "Onboarding completed!"
                        : "Step completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete step");
                return ApiResponse<bool>.ErrorResponse("Failed to complete step", 500);
            }
        }

        // ============================================================
        // FINALIZE ONBOARDING
        // ============================================================
        public async Task<ApiResponse<bool>> CompleteOnboardingAsync()
        {
            try
            {
                var tenant = await _tenantRepo.GetByIdAsync(_currentUser.TenantId);
                if (tenant == null)
                    return ApiResponse<bool>.ErrorResponse("Tenant not found", 404);

                if (tenant.IsOnboardingComplete)
                    return ApiResponse<bool>.SuccessResponse(true, "Already completed");

                if (tenant.OnboardingStep != OnboardingStep.GatewaySetup)
                {
                    return ApiResponse<bool>.ErrorResponse(
                        "Complete the current onboarding step before finishing setup", 409);
                }

                // Validate all required steps
                var validation = await ValidateAllRequiredStepsAsync(tenant);
                if (!validation.Success)
                    return validation;

                tenant.IsOnboardingComplete = true;
                tenant.OnboardingCompletedAt = DateTime.UtcNow;
                tenant.OnboardingStep = OnboardingStep.Completed;

                if (tenant.Status == TenantStatus.Onboarding)
                    tenant.Status = TenantStatus.Active;

                _tenantRepo.Update(tenant);
                await _unitOfWork.SaveChangesAsync();
                ClearOnboardingCache(tenant.Id);

                _logger.LogInformation("Onboarding completed for tenant {Id}", tenant.Id);
                return ApiResponse<bool>.SuccessResponse(true, "Onboarding completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete onboarding");
                return ApiResponse<bool>.ErrorResponse("Failed to complete onboarding", 500);
            }
        }

        // ============================================================
        // INTERNAL: STEP VALIDATION
        // ============================================================
        private async Task<ApiResponse<bool>> ValidateStepCompletionAsync(Tenant tenant, OnboardingStep step)
        {
            switch (step)
            {
                case OnboardingStep.EmailVerification:
                    if (!tenant.IsEmailVerified)
                        return ApiResponse<bool>.ErrorResponse("Email not verified yet", 400);
                    break;

                case OnboardingStep.InstitutionProfile:
                    if (string.IsNullOrWhiteSpace(tenant.Name) ||
                        string.IsNullOrWhiteSpace(tenant.OwnerName) ||
                        string.IsNullOrWhiteSpace(tenant.InstitutionType))
                        return ApiResponse<bool>.ErrorResponse(
                            "Please complete institution profile first", 400);
                    break;

                case OnboardingStep.PlanSelection:
                case OnboardingStep.Payment:
                    var sub = await _subscriptionRepo.GetActiveByTenantAsync(tenant.Id);
                    if (sub == null)
                        return ApiResponse<bool>.ErrorResponse(
                            "Please select a subscription plan first", 400);

                    if (step == OnboardingStep.Payment)
                    {
                        // Trial doesn't need payment
                        if (sub.IsTrial) break;

                        if (sub.Status != SubscriptionStatus.Active &&
                            sub.Status != SubscriptionStatus.Trialing)
                            return ApiResponse<bool>.ErrorResponse(
                                "Please complete payment first", 400);
                    }
                    break;

                case OnboardingStep.CampusSetup:
                    if (!await _campusRepo.GetQueryable()
                            .AsNoTracking()
                            .AnyAsync(x => x.TenantId == tenant.Id))
                    {
                        return ApiResponse<bool>.ErrorResponse(
                            "Please add at least one campus first", 409);
                    }
                    break;

                case OnboardingStep.AcademicSetup:
                    if (!await _academicYearRepo.GetQueryable()
                            .AsNoTracking()
                            .AnyAsync(x => x.TenantId == tenant.Id))
                    {
                        return ApiResponse<bool>.ErrorResponse(
                            "Please add at least one academic year first", 409);
                    }
                    break;

                case OnboardingStep.ModuleSetup:
                    var moduleValidation = await _tenantModuleService
                        .ValidateCurrentTenantSelectionAsync();
                    if (!moduleValidation.Success)
                    {
                        return ApiResponse<bool>.ErrorResponse(
                            moduleValidation.Message ?? "Please review the required modules",
                            moduleValidation.StatusCode);
                    }
                    break;

                case OnboardingStep.BrandingSetup:
                    // Subdomain is required for branding step
                    if (string.IsNullOrWhiteSpace(tenant.Subdomain))
                        return ApiResponse<bool>.ErrorResponse(
                            "Please set your subdomain first", 400);
                    break;

                // Other steps - no strict validation, just advance
                default:
                    break;
            }

            return ApiResponse<bool>.SuccessResponse(true);
        }

        private async Task<ApiResponse<bool>> ValidateAllRequiredStepsAsync(Tenant tenant)
        {
            if (!tenant.IsEmailVerified)
                return ApiResponse<bool>.ErrorResponse("Email verification incomplete", 400);

            if (string.IsNullOrWhiteSpace(tenant.Name))
                return ApiResponse<bool>.ErrorResponse("Institution profile incomplete", 400);

            var sub = await _subscriptionRepo.GetActiveByTenantAsync(tenant.Id);
            if (sub == null)
                return ApiResponse<bool>.ErrorResponse("No active subscription", 400);

            if (!sub.IsTrial &&
                sub.Status != SubscriptionStatus.Active)
                return ApiResponse<bool>.ErrorResponse("Subscription not active", 400);

            if (!await _campusRepo.GetQueryable()
                    .AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant.Id))
                return ApiResponse<bool>.ErrorResponse("Campus setup incomplete", 409);

            if (!await _academicYearRepo.GetQueryable()
                    .AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant.Id))
                return ApiResponse<bool>.ErrorResponse("Academic setup incomplete", 409);

            var moduleValidation = await _tenantModuleService.ValidateCurrentTenantSelectionAsync();
            if (!moduleValidation.Success)
            {
                return ApiResponse<bool>.ErrorResponse(
                    moduleValidation.Message ?? "Module setup incomplete",
                    moduleValidation.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(tenant.Subdomain))
                return ApiResponse<bool>.ErrorResponse("Branding setup incomplete", 409);

            return ApiResponse<bool>.SuccessResponse(true);
        }

        // ============================================================
        // STEP DEFINITIONS
        // ============================================================
        private List<OnboardingStepStatusDto> BuildStepStatusList(Tenant tenant)
        {
            var defs = new List<OnboardingStepStatusDto>
            {
                new()
                {
                    Step = OnboardingStep.EmailVerification,
                    Code = nameof(OnboardingStep.EmailVerification),
                    Order = 1,
                    Name = "Email verification",
                    Description = "Verify your email address",
                    IconClass = "bi-envelope-check",
                    Url = "/Account/VerifyEmail",
                    IsSkippable = false,
                    IsCompleted = tenant.IsEmailVerified,
                },
                new()
                {
                    Step = OnboardingStep.InstitutionProfile,
                    Code = nameof(OnboardingStep.InstitutionProfile),
                    Order = 2,
                    Name = "Institution profile",
                    Description = "Set up your institution details",
                    IconClass = "bi-building",
                    Url = "/Account/InstitutionProfile",
                    IsSkippable = false,
                    IsCompleted = !string.IsNullOrEmpty(tenant.Name) &&
                                  !string.IsNullOrEmpty(tenant.OwnerName) &&
                                  !string.IsNullOrEmpty(tenant.InstitutionType),
                },
                new()
                {
                    Step = OnboardingStep.PlanSelection,
                    Code = nameof(OnboardingStep.PlanSelection),
                    Order = 3,
                    Name = "Choose plan",
                    Description = "Select your subscription plan",
                    IconClass = "bi-tag",
                    Url = "/Account/PlanSelection",
                    IsSkippable = false,
                },
                new()
                {
                    Step = OnboardingStep.Payment,
                    Code = nameof(OnboardingStep.Payment),
                    Order = 4,
                    Name = "Payment",
                    Description = "Complete subscription payment",
                    IconClass = "bi-credit-card",
                    Url = "/Account/Payment",
                    IsSkippable = false,
                },
                new()
                {
                    Step = OnboardingStep.CampusSetup,
                    Code = nameof(OnboardingStep.CampusSetup),
                    Order = 5,
                    Name = "Campus setup",
                    Description = "Configure campus information",
                    IconClass = "bi-geo-alt",
                    Url = "/Account/CampusSetup",
                    IsSkippable = false,
                },
                new()
                {
                    Step = OnboardingStep.AcademicSetup,
                    Code = nameof(OnboardingStep.AcademicSetup),
                    Order = 6,
                    Name = "Academic year",
                    Description = "Set up academic year & terms",
                    IconClass = "bi-calendar3",
                    Url = "/Account/AcademicSetup",
                    IsSkippable = false,
                },
                new()
                {
                    Step = OnboardingStep.ModuleSetup,
                    Code = nameof(OnboardingStep.ModuleSetup),
                    Order = 7,
                    Name = "Module selection",
                    Description = "Choose the tools used by your institution",
                    IconClass = "bi-grid",
                    Url = "/Account/ModuleSetup",
                    IsSkippable = false,
                },
                new()
                {
                    Step = OnboardingStep.BrandingSetup,
                    Code = nameof(OnboardingStep.BrandingSetup),
                    Order = 8,
                    Name = "Branding",
                    Description = "Logo, colors, and subdomain",
                    IconClass = "bi-palette",
                    Url = "/Account/BrandingSetup",
                    IsSkippable = false,
                    IsCompleted = !string.IsNullOrEmpty(tenant.Subdomain),
                },
                new()
                {
                    Step = OnboardingStep.GeneralSettings,
                    Code = nameof(OnboardingStep.GeneralSettings),
                    Order = 9,
                    Name = "General settings",
                    Description = "Currency, timezone, language",
                    IconClass = "bi-gear",
                    Url = "/Account/GeneralSettings",
                    IsSkippable = true,
                },
                new()
                {
                    Step = OnboardingStep.GatewaySetup,
                    Code = nameof(OnboardingStep.GatewaySetup),
                    Order = 10,
                    Name = "SMS / Email setup",
                    Description = "Configure messaging gateways (optional)",
                    IconClass = "bi-chat-dots",
                    Url = "/Account/GatewaySetup",
                    IsSkippable = true,
                },
            };

            var currentOrder = tenant.IsOnboardingComplete
                ? int.MaxValue
                : defs.FirstOrDefault(x => x.Step == tenant.OnboardingStep)?.Order ?? 1;

            foreach (var s in defs)
            {
                s.IsCurrent = !tenant.IsOnboardingComplete && s.Step == tenant.OnboardingStep;
                s.IsCompleted = tenant.IsOnboardingComplete || s.IsCompleted || s.Order < currentOrder;
                s.IsLocked = !tenant.IsOnboardingComplete && s.Order > currentOrder;
            }

            return defs;
        }

        private static OnboardingStep GetNextStep(OnboardingStep current)
        {
            return current switch
            {
                OnboardingStep.EmailVerification => OnboardingStep.InstitutionProfile,
                OnboardingStep.InstitutionProfile => OnboardingStep.PlanSelection,
                OnboardingStep.PlanSelection => OnboardingStep.Payment,
                OnboardingStep.Payment => OnboardingStep.CampusSetup,
                OnboardingStep.CampusSetup => OnboardingStep.AcademicSetup,
                OnboardingStep.AcademicSetup => OnboardingStep.ModuleSetup,
                OnboardingStep.ModuleSetup => OnboardingStep.BrandingSetup,
                OnboardingStep.BrandingSetup => OnboardingStep.GeneralSettings,
                OnboardingStep.GeneralSettings => OnboardingStep.GatewaySetup,
                OnboardingStep.GatewaySetup => OnboardingStep.Completed,
                _ => OnboardingStep.Completed
            };
        }

        private static int GetStepOrder(OnboardingStep step)
        {
            return step switch
            {
                OnboardingStep.EmailVerification => 1,
                OnboardingStep.InstitutionProfile => 2,
                OnboardingStep.PlanSelection => 3,
                OnboardingStep.Payment => 4,
                OnboardingStep.CampusSetup => 5,
                OnboardingStep.AcademicSetup => 6,
                OnboardingStep.ModuleSetup => 7,
                OnboardingStep.BrandingSetup => 8,
                OnboardingStep.GeneralSettings => 9,
                OnboardingStep.GatewaySetup => 10,
                OnboardingStep.Completed => 11,
                _ => 0
            };
        }

        private static bool IsSkippable(OnboardingStep step) =>
            step is OnboardingStep.GeneralSettings or OnboardingStep.GatewaySetup;

        private void ClearOnboardingCache(long tenantId) =>
            _cache.Remove($"onboarding:tenant:{tenantId}");
    }
}
