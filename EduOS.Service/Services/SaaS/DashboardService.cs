using EduOS.Core.Common;
using EduOS.Core.DTOs.Dashboard;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS
{
    public class DashboardService : IDashboardService
    {
        private readonly IGenericRepository<Tenant> _tenantRepo;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<DashboardService> _logger;

        // Feature count query — reads PlanFeatures for the tenant's active plan
        private readonly ISubscriptionPlanRepository _planRepo;
        private readonly ITenantSubscriptionRepository _subscriptionRepo;

        // These are optional — if you don't have them yet, comment out
        // private readonly IGenericRepository<Student> _studentRepo;
        // private readonly IGenericRepository<HREmployee> _employeeRepo;
        // private readonly IGenericRepository<Payment> _paymentRepo;
        // private readonly IGenericRepository<StudentInvoice> _invoiceRepo;
        // private readonly IGenericRepository<Campus> _campusRepo;
        // private readonly IGenericRepository<Class> _classRepo;

        public DashboardService(
            IGenericRepository<Tenant> tenantRepo,
            ICurrentUserService currentUser,
            ILogger<DashboardService> logger,
            ISubscriptionPlanRepository planRepo,
            ITenantSubscriptionRepository subscriptionRepo)
        {
            _tenantRepo = tenantRepo;
            _currentUser = currentUser;
            _logger = logger;
            _planRepo = planRepo;
            _subscriptionRepo = subscriptionRepo;
        }

        public async Task<ApiResponse<DashboardVm>> GetDashboardAsync()
        {
            try
            {
                var tenantId = _currentUser.TenantId;
                if (tenantId <= 0)
                    return ApiResponse<DashboardVm>.ErrorResponse("Tenant not found", 401);

                // ── 1. Load tenant ─────────────────────────────────────────
                var tenant = await _tenantRepo.GetByIdAsync(tenantId);
                if (tenant == null)
                    return ApiResponse<DashboardVm>.ErrorResponse("Institution not found", 404);

                // ── 2. Load active subscription ────────────────────────────
                var subscription = await _subscriptionRepo.GetActiveByTenantAsync(tenantId);
                string planName = "No plan";
                string? planNameBangla = null;
                string planCode = string.Empty;
                int featureCount = 0;

                if (subscription?.SubscriptionPlan != null)
                {
                    planName = subscription.SubscriptionPlan.Name;
                    planNameBangla = subscription.SubscriptionPlan.NameBangla;
                    planCode = subscription.SubscriptionPlan.Code;
                    featureCount = subscription.SubscriptionPlan.PlanFeatures
                        .Count(pf => pf.IsEnabled);
                }
                else if (subscription != null)
                {
                    // Load plan separately if not included
                    var plan = await _planRepo.GetWithFeaturesAsync(subscription.SubscriptionPlanId);
                    if (plan != null)
                    {
                        planName = plan.Name;
                        planNameBangla = plan.NameBangla;
                        planCode = plan.Code;
                        featureCount = plan.PlanFeatures.Count(pf => pf.IsEnabled);
                    }
                }

                // ── 3. Calculate trial / expiry info ───────────────────────
                int? trialDaysRemaining = null;
                int daysUntilExpiry = 0;

                if (subscription != null)
                {
                    if (subscription.IsTrial && subscription.TrialEndDate.HasValue)
                    {
                        var diff = (subscription.TrialEndDate.Value - DateTime.UtcNow).TotalDays;
                        trialDaysRemaining = Math.Max(0, (int)Math.Ceiling(diff));
                    }

                    if (subscription.EndDate > DateTime.UtcNow)
                    {
                        var diff = (subscription.EndDate - DateTime.UtcNow).TotalDays;
                        daysUntilExpiry = (int)Math.Ceiling(diff);
                    }
                }

                // ── 4. Onboarding progress ─────────────────────────────────
                var onboardingPercent = CalculateOnboardingPercent(tenant);

                // ── 5. Stats (real counts — extend when repos are available) 
                // For now we use cached counts on the Tenant entity.
                // When you inject the actual repositories, replace these.
                var totalStudents = tenant.CurrentStudents;
                var totalTeachers = tenant.CurrentTeachers;

                // ── 6. Build alerts ────────────────────────────────────────
                var alerts = BuildAlerts(tenant, subscription, trialDaysRemaining, daysUntilExpiry);

                // ── 7. Compose view model ──────────────────────────────────
                var vm = new DashboardVm
                {
                    // Institution
                    InstitutionName = tenant.Name,
                    InstitutionType = tenant.InstitutionType,
                    OwnerName = tenant.OwnerName,
                    LogoUrl = tenant.LogoUrl,

                    // Subscription
                    PlanName = planName,
                    PlanNameBangla = planNameBangla,
                    PlanCode = planCode,
                    IsTrialActive = tenant.IsTrialActive,
                    TrialEndDate = subscription?.TrialEndDate,
                    TrialDaysRemaining = trialDaysRemaining,
                    SubscriptionEndDate = subscription?.EndDate,
                    DaysUntilExpiry = daysUntilExpiry,
                    SubscriptionStatus = subscription?.Status.ToString() ?? "None",

                    // Onboarding
                    EmailVerified = tenant.IsEmailVerified,
                    OnboardingComplete = tenant.IsOnboardingComplete,
                    OnboardingStep = (int)tenant.OnboardingStep,
                    OnboardingPercent = onboardingPercent,

                    // Limits
                    MaxStudents = tenant.MaxStudents,
                    CurrentStudents = tenant.CurrentStudents,
                    MaxTeachers = tenant.MaxTeachers,
                    CurrentTeachers = tenant.CurrentTeachers,
                    MaxCampuses = tenant.MaxCampuses,
                    ActiveFeatures = featureCount,

                    // Stats
                    TotalStudents = totalStudents,
                    TotalTeachers = totalTeachers,
                    TotalStaff = 0,         // Extend: await _employeeRepo...
                    TotalCampuses = 0,      // Extend: await _campusRepo...
                    TotalClasses = 0,       // Extend: await _classRepo...
                    MonthlyCollection = 0,  // Extend: await _paymentRepo...
                    TotalDues = 0,          // Extend: await _invoiceRepo...

                    Alerts = alerts
                };

                return ApiResponse<DashboardVm>.SuccessResponse(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DashboardService.GetDashboardAsync failed for tenant {TenantId}",
                    _currentUser.TenantId);
                return ApiResponse<DashboardVm>.ErrorResponse("Failed to load dashboard", 500);
            }
        }

        // ── Onboarding percent ────────────────────────────────────────────
        private static int CalculateOnboardingPercent(Tenant tenant)
        {
            // 9 total onboarding steps (0–8), Completed = 99
            if (tenant.IsOnboardingComplete) return 100;

            var step = (int)tenant.OnboardingStep;
            const int totalSteps = 9; // EmailVerification(0) → GatewaySetup(8)

            if (step >= 99) return 100;

            return (int)Math.Round((double)step / totalSteps * 100);
        }

        // ── Smart alerts builder ──────────────────────────────────────────
        private static List<DashboardAlert> BuildAlerts(
            Tenant tenant,
            EduOS.Core.Entities.SaaS.TenantSubscription? subscription,
            int? trialDaysRemaining,
            int daysUntilExpiry)
        {
            var alerts = new List<DashboardAlert>();

            // Email not verified
            if (!tenant.IsEmailVerified)
            {
                alerts.Add(new DashboardAlert
                {
                    Code = "EMAIL_UNVERIFIED",
                    Type = "warning",
                    Message = "Please verify your email address to unlock all features.",
                    ActionUrl = "/Account/VerifyEmail",
                    ActionCode = "VERIFY_EMAIL",
                    ActionLabel = "Verify email"
                });
            }

            // Onboarding incomplete
            if (!tenant.IsOnboardingComplete)
            {
                alerts.Add(new DashboardAlert
                {
                    Code = "ONBOARDING_INCOMPLETE",
                    Type = "info",
                    Message = "Your institution setup is not complete. Some features may be limited.",
                    ActionUrl = "/Account/InstitutionProfile",
                    ActionCode = "CONTINUE_SETUP",
                    ActionLabel = "Continue setup"
                });
            }

            // No subscription
            if (subscription == null)
            {
                alerts.Add(new DashboardAlert
                {
                    Code = "SUBSCRIPTION_MISSING",
                    Type = "danger",
                    Message = "No active subscription found. Please choose a plan to continue using EduOS.",
                    ActionUrl = "/Account/PlanSelection",
                    ActionCode = "CHOOSE_PLAN",
                    ActionLabel = "Choose a plan"
                });
                return alerts; // No point showing other alerts
            }

            // Trial expiring soon (≤ 3 days)
            if (tenant.IsTrialActive && trialDaysRemaining.HasValue)
            {
                if (trialDaysRemaining.Value == 0)
                {
                    alerts.Add(new DashboardAlert
                    {
                        Code = "TRIAL_EXPIRED",
                        Type = "danger",
                        Message = "Your free trial has expired. Upgrade now to keep your data.",
                        ActionUrl = "/Account/PlanSelection",
                        ActionCode = "UPGRADE_NOW",
                        ActionLabel = "Upgrade now"
                    });
                }
                else if (trialDaysRemaining.Value <= 3)
                {
                    alerts.Add(new DashboardAlert
                    {
                        Code = "TRIAL_EXPIRING",
                        Type = "warning",
                        Message = $"Your free trial expires in {trialDaysRemaining.Value} day(s). Upgrade to keep access.",
                        ActionUrl = "/Account/PlanSelection",
                        ActionCode = "UPGRADE_NOW",
                        Days = trialDaysRemaining.Value,
                        ActionLabel = "Upgrade now"
                    });
                }
                else if (trialDaysRemaining.Value <= 7)
                {
                    alerts.Add(new DashboardAlert
                    {
                        Code = "TRIAL_ENDING",
                        Type = "info",
                        Message = $"Your free trial ends in {trialDaysRemaining.Value} days.",
                        ActionUrl = "/Account/PlanSelection",
                        ActionCode = "VIEW_PLANS",
                        Days = trialDaysRemaining.Value,
                        ActionLabel = "View plans"
                    });
                }
            }

            // Paid subscription expiring soon (≤ 7 days)
            if (!tenant.IsTrialActive && daysUntilExpiry > 0 && daysUntilExpiry <= 7)
            {
                alerts.Add(new DashboardAlert
                {
                    Code = "SUBSCRIPTION_EXPIRING",
                    Type = daysUntilExpiry <= 3 ? "danger" : "warning",
                    Message = $"Your subscription expires in {daysUntilExpiry} day(s). Renew to avoid interruption.",
                    ActionUrl = "/Account/PlanSelection",
                    ActionCode = "RENEW_NOW",
                    Days = daysUntilExpiry,
                    ActionLabel = "Renew now"
                });
            }

            // Subscription expired
            if (subscription.Status == EduOS.Core.Enums.SubscriptionStatus.Expired)
            {
                alerts.Add(new DashboardAlert
                {
                    Code = "SUBSCRIPTION_EXPIRED",
                    Type = "danger",
                    Message = "Your subscription has expired. Renew now to restore full access.",
                    ActionUrl = "/Account/PlanSelection",
                    ActionCode = "RENEW_SUBSCRIPTION",
                    ActionLabel = "Renew subscription"
                });
            }

            // Awaiting manual payment verification
            if (subscription.Status == EduOS.Core.Enums.SubscriptionStatus.PendingPayment)
            {
                alerts.Add(new DashboardAlert
                {
                    Code = "PAYMENT_PENDING",
                    Type = "warning",
                    Message = "Your payment is being verified. Access will activate once confirmed.",
                    ActionUrl = "/Account/PlanSelection",
                    ActionCode = "VIEW_PAYMENT_STATUS",
                    ActionLabel = "View payment status"
                });
            }

            // Student limit warning (>= 90%)
            if (tenant.MaxStudents > 0)
            {
                var pct = (double)tenant.CurrentStudents / tenant.MaxStudents * 100;
                if (pct >= 90)
                {
                    alerts.Add(new DashboardAlert
                    {
                        Code = pct >= 100 ? "STUDENT_LIMIT_REACHED" : "STUDENT_LIMIT_WARNING",
                        Type = pct >= 100 ? "danger" : "warning",
                        Message = pct >= 100
                            ? $"Student limit reached ({tenant.CurrentStudents}/{tenant.MaxStudents}). Upgrade your plan."
                            : $"You are at {(int)pct}% of your student limit ({tenant.CurrentStudents}/{tenant.MaxStudents}).",
                        ActionUrl = "/Account/PlanSelection",
                        ActionCode = "UPGRADE_PLAN",
                        Percentage = (int)pct,
                        CurrentValue = tenant.CurrentStudents,
                        LimitValue = tenant.MaxStudents,
                        ActionLabel = "Upgrade plan"
                    });
                }
            }

            return alerts;
        }
    }
}
