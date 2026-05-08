using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Service.Helpers.Subscription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduOS.Service.Services.SaaS
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ITenantSubscriptionRepository _subscriptionRepo;
        private readonly ISubscriptionPlanRepository _planRepo;
        private readonly ISubscriptionInvoiceRepository _invoiceRepo;
        private readonly IGenericRepository<EduOS.Core.Entities.Tenants.Tenant> _tenantRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IMapper _mapper;
        private readonly ManualPaymentSettings _manualSettings;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(
            ITenantSubscriptionRepository subscriptionRepo,
            ISubscriptionPlanRepository planRepo,
            ISubscriptionInvoiceRepository invoiceRepo,
            IGenericRepository<EduOS.Core.Entities.Tenants.Tenant> tenantRepo,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IMapper mapper,
            IOptions<ManualPaymentSettings> manualSettings,
            ILogger<SubscriptionService> logger)
        {
            _subscriptionRepo = subscriptionRepo;
            _planRepo = planRepo;
            _invoiceRepo = invoiceRepo;
            _tenantRepo = tenantRepo;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _mapper = mapper;
            _manualSettings = manualSettings.Value;
            _logger = logger;
        }

        // ============================================================
        // CREATE
        // ============================================================
        public async Task<ApiResponse<CreateSubscriptionResponseDto>> CreateAsync(
            CreateSubscriptionRequestDto dto)
        {
            var tenantId = _currentUser.TenantId;

            if (tenantId <= 0)
            {
                return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse(
                    "Tenant context required",
                    401);
            }

            var strategy = _unitOfWork.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await _unitOfWork.BeginTransactionAsync();

                try
                {
                    // 1. Load plan
                    var plan = await _planRepo.GetByIdAsync(dto.SubscriptionPlanId);

                    if (plan == null || !plan.IsActive)
                    {
                        await _unitOfWork.RollbackTransactionAsync();

                        return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse(
                            "Plan not found",
                            404);
                    }

                    // 2. Block if tenant already has active non-trial subscription
                    var existing = await _subscriptionRepo.GetActiveByTenantAsync(tenantId);

                    if (existing != null &&
                        !existing.IsTrial &&
                        existing.Status == SubscriptionStatus.Active)
                    {
                        await _unitOfWork.RollbackTransactionAsync();

                        return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse(
                            "You already have an active subscription. Please cancel it before subscribing to a new plan.",
                            400);
                    }

                    // 3. Load tenant
                    var tenant = await _tenantRepo.GetByIdAsync(tenantId);

                    if (tenant == null)
                    {
                        await _unitOfWork.RollbackTransactionAsync();

                        return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse(
                            "Tenant not found",
                            404);
                    }

                    var now = DateTime.UtcNow;
                    var isTrial = plan.IsFreeTrial;

                    var price = isTrial
                        ? 0
                        : SubscriptionCalculator.GetPriceForCycle(plan, dto.BillingCycle);

                    // 4. Build subscription record
                    var subscription = new TenantSubscription
                    {
                        TenantId = tenantId,
                        SubscriptionPlanId = plan.Id,
                        BillingCycle = dto.BillingCycle,
                        AutoRenew = dto.AutoRenew,
                        Currency = plan.Currency,
                        MaxStudents = plan.MaxStudents,
                        MaxTeachers = plan.MaxTeachers,
                        MaxCampuses = plan.MaxCampuses,
                        MaxStorageMb = plan.MaxStorageMb,
                        StartDate = now,
                        Price = price,
                        DiscountAmount = 0,
                        TaxAmount = 0,
                        FinalAmount = price
                    };

                    if (isTrial)
                    {
                        var trialDays = plan.TrialDays ?? 14;

                        subscription.IsTrial = true;
                        subscription.TrialStartDate = now;
                        subscription.TrialEndDate = SubscriptionCalculator.CalculateTrialEndDate(
                            now,
                            trialDays);
                        subscription.EndDate = subscription.TrialEndDate.Value;
                        subscription.Status = SubscriptionStatus.Trialing;
                    }
                    else
                    {
                        subscription.EndDate = SubscriptionCalculator.CalculateEndDate(
                            now,
                            dto.BillingCycle);
                        subscription.Status = SubscriptionStatus.PendingPayment;
                    }

                    subscription.NextBillingDate = subscription.EndDate;

                    await _subscriptionRepo.AddAsync(subscription);
                    await _unitOfWork.SaveChangesAsync();

                    // 5. Generate invoice only for paid plans
                    SubscriptionInvoice? invoice = null;

                    if (!isTrial && price > 0)
                    {
                        invoice = new SubscriptionInvoice
                        {
                            TenantId = tenantId,
                            TenantSubscriptionId = subscription.Id,
                            InvoiceNumber = await _invoiceRepo.GenerateNextInvoiceNumberAsync(),
                            IssueDate = now,
                            DueDate = now.AddDays(7),
                            PeriodStart = now,
                            PeriodEnd = subscription.EndDate,
                            Subtotal = price,
                            DiscountAmount = 0,
                            TaxAmount = 0,
                            TotalAmount = price,
                            PaidAmount = 0,
                            DueAmount = price,
                            Currency = plan.Currency,
                            PaymentStatus = PaymentStatus.Pending,
                            CustomerName = tenant.Name,
                            CustomerEmail = tenant.Email,
                            CustomerPhone = tenant.Phone,
                            CustomerAddress = tenant.Address,
                            Description = $"{plan.Name} subscription - {dto.BillingCycle}"
                        };

                        await _invoiceRepo.AddAsync(invoice);
                        await _unitOfWork.SaveChangesAsync();
                    }

                    // 6. Update tenant
                    tenant.CurrentSubscriptionId = subscription.Id;
                    tenant.MaxStudents = plan.MaxStudents;
                    tenant.MaxTeachers = plan.MaxTeachers;
                    tenant.MaxCampuses = plan.MaxCampuses;
                    tenant.MaxStorageMb = plan.MaxStorageMb;
                    tenant.SubscriptionEndsAt = subscription.EndDate;

                    if (isTrial)
                    {
                        tenant.Status = TenantStatus.Trial;
                        tenant.IsTrialActive = true;
                        tenant.TrialEndsAt = subscription.TrialEndDate;
                    }

                    _tenantRepo.Update(tenant);
                    await _unitOfWork.SaveChangesAsync();

                    await _unitOfWork.CommitTransactionAsync();

                    _logger.LogInformation(
                        "Subscription {SubscriptionId} created for tenant {TenantId} with plan {PlanCode}",
                        subscription.Id,
                        tenantId,
                        plan.Code);

                    // 7. Build response
                    var response = new CreateSubscriptionResponseDto
                    {
                        SubscriptionId = subscription.Id,
                        InvoiceId = invoice?.Id,
                        InvoiceNumber = invoice?.InvoiceNumber,
                        Amount = subscription.FinalAmount,
                        Currency = subscription.Currency,
                        Status = subscription.Status,
                        IsTrialActivated = isTrial,
                        TrialEndsAt = subscription.TrialEndDate,
                        Message = isTrial
                            ? $"Your {plan.TrialDays ?? 14}-day free trial has started. Enjoy!"
                            : "Subscription created. Please complete payment to activate."
                    };

                    if (!isTrial)
                    {
                        response.ManualPaymentInstructions = new ManualPaymentInstructionsDto
                        {
                            BankName = _manualSettings.BankName,
                            AccountName = _manualSettings.AccountName,
                            AccountNumber = _manualSettings.AccountNumber,
                            RoutingNumber = _manualSettings.RoutingNumber,
                            BranchName = _manualSettings.BranchName,
                            Reference = invoice?.InvoiceNumber ?? string.Empty,
                            Instructions = _manualSettings.Instructions
                        };
                    }

                    return ApiResponse<CreateSubscriptionResponseDto>.SuccessResponse(
                        response,
                        "Subscription created successfully");
                }
                catch (Exception ex)
                {
                    await _unitOfWork.RollbackTransactionAsync();

                    _logger.LogError(
                        ex,
                        "Failed to create subscription for tenant {TenantId}",
                        tenantId);

                    return ApiResponse<CreateSubscriptionResponseDto>.ErrorResponse(
                        "Failed to create subscription",
                        500);
                }
            });
        }
        // ============================================================
        // GET CURRENT
        // ============================================================
        public async Task<ApiResponse<CurrentSubscriptionDto>> GetCurrentAsync()
        {
            var tenantId = _currentUser.TenantId;
            try
            {
                var subscription = await _subscriptionRepo.GetActiveByTenantAsync(tenantId);
                if (subscription == null)
                    return ApiResponse<CurrentSubscriptionDto>.ErrorResponse("No active subscription", 404);

                var tenant = await _tenantRepo.GetByIdAsync(tenantId);

                var dto = new CurrentSubscriptionDto
                {
                    Id = subscription.Id,
                    PlanId = subscription.SubscriptionPlanId,
                    PlanName = subscription.SubscriptionPlan?.Name ?? string.Empty,
                    PlanCode = subscription.SubscriptionPlan?.Code ?? string.Empty,
                    BillingCycle = subscription.BillingCycle,
                    Status = subscription.Status,
                    StartDate = subscription.StartDate,
                    EndDate = subscription.EndDate,
                    NextBillingDate = subscription.NextBillingDate,
                    IsTrial = subscription.IsTrial,
                    TrialEndDate = subscription.TrialEndDate,
                    TrialDaysRemaining = subscription.IsTrial && subscription.TrialEndDate.HasValue
                        ? SubscriptionCalculator.CalculateDaysRemaining(subscription.TrialEndDate.Value)
                        : null,
                    Price = subscription.Price,
                    FinalAmount = subscription.FinalAmount,
                    Currency = subscription.Currency,
                    AutoRenew = subscription.AutoRenew,
                    CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
                    MaxStudents = subscription.MaxStudents,
                    CurrentStudents = tenant?.CurrentStudents ?? 0,
                    MaxTeachers = subscription.MaxTeachers,
                    CurrentTeachers = tenant?.CurrentTeachers ?? 0,
                    MaxCampuses = subscription.MaxCampuses,
                    CurrentCampuses = 0, // TODO: count campuses
                    DaysRemaining = SubscriptionCalculator.CalculateDaysRemaining(subscription.EndDate)
                };

                return ApiResponse<CurrentSubscriptionDto>.SuccessResponse(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current subscription for tenant {TenantId}", tenantId);
                return ApiResponse<CurrentSubscriptionDto>.ErrorResponse("Failed to load subscription", 500);
            }
        }

        // ============================================================
        // GET HISTORY
        // ============================================================
        public async Task<ApiResponse<List<SubscriptionHistoryDto>>> GetHistoryAsync()
        {
            var tenantId = _currentUser.TenantId;
            try
            {
                var subs = await _subscriptionRepo.GetHistoryByTenantAsync(tenantId);
                var dtos = subs.Select(s => new SubscriptionHistoryDto
                {
                    Id = s.Id,
                    PlanName = s.SubscriptionPlan?.Name ?? string.Empty,
                    BillingCycle = s.BillingCycle,
                    Status = s.Status,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    FinalAmount = s.FinalAmount,
                    Currency = s.Currency
                }).ToList();

                return ApiResponse<List<SubscriptionHistoryDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load subscription history");
                return ApiResponse<List<SubscriptionHistoryDto>>.ErrorResponse("Failed to load history", 500);
            }
        }

        // ============================================================
        // CANCEL
        // ============================================================
        public async Task<ApiResponse<bool>> CancelAsync(
            long subscriptionId, string? reason, bool cancelAtPeriodEnd = true)
        {
            var tenantId = _currentUser.TenantId;
            try
            {
                var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId);
                if (subscription == null || subscription.TenantId != tenantId)
                    return ApiResponse<bool>.ErrorResponse("Subscription not found", 404);

                if (subscription.Status == SubscriptionStatus.Cancelled)
                    return ApiResponse<bool>.ErrorResponse("Already cancelled", 400);

                if (cancelAtPeriodEnd)
                {
                    subscription.CancelAtPeriodEnd = true;
                    subscription.Status = SubscriptionStatus.CancelAtPeriodEnd;
                    subscription.AutoRenew = false;
                }
                else
                {
                    subscription.Status = SubscriptionStatus.Cancelled;
                    subscription.CancelledAt = DateTime.UtcNow;
                    subscription.AutoRenew = false;
                    subscription.EndDate = DateTime.UtcNow;
                }

                subscription.CancellationReason = reason;
                _subscriptionRepo.Update(subscription);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Subscription {Id} cancelled (atPeriodEnd={AtPeriodEnd})",
                    subscriptionId, cancelAtPeriodEnd);

                return ApiResponse<bool>.SuccessResponse(true,
                    cancelAtPeriodEnd
                        ? "Subscription will be cancelled at period end"
                        : "Subscription cancelled immediately");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel subscription {Id}", subscriptionId);
                return ApiResponse<bool>.ErrorResponse("Cancellation failed", 500);
            }
        }

        // ============================================================
        // TOGGLE AUTO-RENEW
        // ============================================================
        public async Task<ApiResponse<bool>> ToggleAutoRenewAsync(long subscriptionId, bool autoRenew)
        {
            var tenantId = _currentUser.TenantId;
            try
            {
                var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId);
                if (subscription == null || subscription.TenantId != tenantId)
                    return ApiResponse<bool>.ErrorResponse("Subscription not found", 404);

                subscription.AutoRenew = autoRenew;
                _subscriptionRepo.Update(subscription);
                await _unitOfWork.SaveChangesAsync();

                return ApiResponse<bool>.SuccessResponse(true,
                    autoRenew ? "Auto-renew enabled" : "Auto-renew disabled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle auto-renew for {Id}", subscriptionId);
                return ApiResponse<bool>.ErrorResponse("Operation failed", 500);
            }
        }

        // ============================================================
        // ACTIVATE AFTER PAYMENT (called by PaymentService)
        // ============================================================
        public async Task<ApiResponse<bool>> ActivateAfterPaymentAsync(long subscriptionId)
        {
            try
            {
                var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId);
                if (subscription == null)
                    return ApiResponse<bool>.ErrorResponse("Subscription not found", 404);

                if (subscription.Status == SubscriptionStatus.Active)
                    return ApiResponse<bool>.SuccessResponse(true, "Already active");

                subscription.Status = SubscriptionStatus.Active;
                _subscriptionRepo.Update(subscription);

                // Update tenant status too
                var tenant = await _tenantRepo.GetByIdAsync(subscription.TenantId);
                if (tenant != null)
                {
                    tenant.Status = TenantStatus.Active;
                    tenant.IsTrialActive = false;
                    tenant.ActivatedAt ??= DateTime.UtcNow;
                    _tenantRepo.Update(tenant);
                }

                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Subscription {Id} activated for tenant {TenantId}",
                    subscriptionId, subscription.TenantId);

                return ApiResponse<bool>.SuccessResponse(true, "Subscription activated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to activate subscription {Id}", subscriptionId);
                return ApiResponse<bool>.ErrorResponse("Activation failed", 500);
            }
        }

        // ============================================================
        // CHECK EXPIRY (called by Hangfire daily)
        // ============================================================
        public async Task<ApiResponse<bool>> CheckExpiryAsync(long tenantId)
        {
            try
            {
                var subscription = await _subscriptionRepo.GetActiveByTenantAsync(tenantId);
                if (subscription == null) return ApiResponse<bool>.SuccessResponse(true);

                if (subscription.EndDate < DateTime.UtcNow &&
                    subscription.Status != SubscriptionStatus.Expired)
                {
                    subscription.Status = SubscriptionStatus.Expired;
                    _subscriptionRepo.Update(subscription);

                    var tenant = await _tenantRepo.GetByIdAsync(tenantId);
                    if (tenant != null)
                    {
                        tenant.Status = TenantStatus.Expired;
                        tenant.IsTrialActive = false;
                        _tenantRepo.Update(tenant);
                    }

                    await _unitOfWork.SaveChangesAsync();
                    _logger.LogInformation("Subscription {Id} marked expired for tenant {TenantId}",
                        subscription.Id, tenantId);
                }

                return ApiResponse<bool>.SuccessResponse(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expiry check failed for tenant {TenantId}", tenantId);
                return ApiResponse<bool>.ErrorResponse("Check failed", 500);
            }
        }
    }
}
