using EduOS.Core.Common;
using EduOS.Core.DTOs.Dashboard;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

public sealed class DashboardService : IDashboardService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<InstitutionTypeDefinition> _institutionTypes;
    private readonly IGenericRepository<SubscriptionPlan> _plans;
    private readonly IGenericRepository<PlanFeature> _planFeatures;
    private readonly ITenantSubscriptionRepository _subscriptions;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IGenericRepository<StudentPayment> _payments;
    private readonly IGenericRepository<StudentInvoice> _invoices;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(IGenericRepository<Tenant> tenants,
        IGenericRepository<InstitutionTypeDefinition> institutionTypes,
        IGenericRepository<SubscriptionPlan> plans, IGenericRepository<PlanFeature> planFeatures,
        ITenantSubscriptionRepository subscriptions,
        IGenericRepository<Student> students, IGenericRepository<Employee> employees,
        IGenericRepository<Campus> campuses, IGenericRepository<AcademicLevel> levels,
        IGenericRepository<StudentPayment> payments, IGenericRepository<StudentInvoice> invoices,
        ICurrentUserService currentUser, ILogger<DashboardService> logger)
    {
        _tenants = tenants; _institutionTypes = institutionTypes;
        _plans = plans; _planFeatures = planFeatures; _subscriptions = subscriptions;
        _students = students; _employees = employees; _campuses = campuses;
        _levels = levels; _payments = payments; _invoices = invoices;
        _currentUser = currentUser; _logger = logger;
    }

    public async Task<ApiResponse<DashboardVm>> GetDashboardAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId <= 0)
            return ApiResponse<DashboardVm>.ErrorResponse("Tenant access is required.", 403);

        var tenantId = _currentUser.TenantId;
        try
        {
            var tenant = await _tenants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId);
            if (tenant == null) return ApiResponse<DashboardVm>.ErrorResponse("Institution was not found.", 404);
            var subscription = await _subscriptions.GetActiveByTenantAsync(tenantId);
            var plan = subscription == null ? null : await _plans.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == subscription.SubscriptionPlanId);
            var institutionType = tenant.InstitutionTypeDefinitionId.HasValue
                ? await _institutionTypes.GetQueryable().AsNoTracking().Where(x => x.Id == tenant.InstitutionTypeDefinitionId.Value)
                    .Select(x => x.Name).FirstOrDefaultAsync()
                : null;
            var enabledFeatures = plan == null ? 0 : await _planFeatures.GetQueryable().AsNoTracking()
                .CountAsync(x => x.SubscriptionPlanId == plan.Id && x.IsEnabled);

            var totalStudents = await _students.GetQueryable().AsNoTracking()
                .CountAsync(x => x.TenantId == tenantId && x.IsActive);
            var activeEmployees = _employees.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.State == EmployeeState.Active);
            var totalTeachers = await activeEmployees.CountAsync(x => x.CanTeach);
            var totalEmployees = await activeEmployees.CountAsync();
            var totalCampuses = await _campuses.GetQueryable().AsNoTracking()
                .CountAsync(x => x.TenantId == tenantId && x.IsActive);
            var totalLevels = await _levels.GetQueryable().AsNoTracking()
                .CountAsync(x => x.TenantId == tenantId && x.IsActive);

            var nowUtc = DateTime.UtcNow;
            var localNow = nowUtc;
            try
            {
                if (!string.IsNullOrWhiteSpace(tenant.TimeZoneId))
                    localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZoneId));
            }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("Tenant {TenantId} has an unknown timezone; UTC fallback used.", tenantId);
            }
            catch (InvalidTimeZoneException)
            {
                _logger.LogWarning("Tenant {TenantId} has invalid timezone configuration; UTC fallback used.", tenantId);
            }
            var monthStart = new DateOnly(localNow.Year, localNow.Month, 1);
            var nextMonth = monthStart.AddMonths(1);
            var monthlyCollection = await _payments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.State == PaymentState.Successful &&
                    x.PaymentDate >= monthStart && x.PaymentDate < nextMonth)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;
            var totalDues = await _invoices.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.State != InvoiceState.Cancelled &&
                    x.State != InvoiceState.Refunded && x.DueAmount > 0m)
                .SumAsync(x => (decimal?)x.DueAmount) ?? 0m;

            var expiryDays = subscription == null ? 0 : Math.Max(0, (int)Math.Ceiling((subscription.EndsAt - nowUtc).TotalDays));
            var trialDays = subscription?.IsTrial == true ? expiryDays : (int?)null;
            var onboardingStep = Enum.TryParse<OnboardingStep>(tenant.OnboardingStage.ToString(), out var legacyStep)
                ? legacyStep : OnboardingStep.EmailVerification;
            var stageNo = Math.Clamp((int)tenant.OnboardingStage - 1, 0, 10);

            var vm = new DashboardVm
            {
                InstitutionName = tenant.Name, InstitutionType = institutionType,
                LogoUrl = tenant.LogoUrl, OwnerName = null,
                PlanName = plan?.Name ?? "No plan", PlanCode = plan?.Code ?? string.Empty,
                IsTrialActive = subscription?.IsTrial == true,
                TrialEndDate = subscription?.IsTrial == true ? subscription.EndsAt : null,
                TrialDaysRemaining = trialDays, SubscriptionEndDate = subscription?.EndsAt,
                DaysUntilExpiry = expiryDays, SubscriptionStatus = subscription?.State.ToString() ?? "None",
                EmailVerified = tenant.IsEmailVerified, OnboardingComplete = tenant.IsOnboardingComplete,
                OnboardingStep = (int)onboardingStep,
                OnboardingPercent = tenant.IsOnboardingComplete ? 100 : stageNo * 10,
                MaxStudents = plan?.MaxStudents ?? 0, CurrentStudents = totalStudents,
                MaxTeachers = 0, CurrentTeachers = totalTeachers,
                MaxCampuses = plan?.MaxCampuses ?? 0, ActiveFeatures = enabledFeatures,
                TotalStudents = totalStudents, TotalTeachers = totalTeachers,
                TotalStaff = Math.Max(0, totalEmployees - totalTeachers),
                TotalCampuses = totalCampuses, TotalClasses = totalLevels,
                MonthlyCollection = monthlyCollection, TotalDues = totalDues
            };
            vm.Alerts = BuildAlerts(tenant, subscription, plan, totalStudents, trialDays, expiryDays);
            return ApiResponse<DashboardVm>.SuccessResponse(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant dashboard query failed for tenant {TenantId}", tenantId);
            return ApiResponse<DashboardVm>.ErrorResponse("Dashboard could not be loaded.", 500);
        }
    }

    private static List<DashboardAlert> BuildAlerts(Tenant tenant, TenantSubscription? subscription,
        SubscriptionPlan? plan, int studentCount, int? trialDays, int expiryDays)
    {
        var items = new List<DashboardAlert>();
        if (!tenant.IsEmailVerified)
            items.Add(new DashboardAlert { Code = "EMAIL_UNVERIFIED", Type = "warning",
                Message = "Verify your institution email.", ActionUrl = "/Account/VerifyEmail", ActionLabel = "Verify email" });
        if (!tenant.IsOnboardingComplete)
            items.Add(new DashboardAlert { Code = "ONBOARDING_INCOMPLETE", Type = "info",
                Message = "Complete the institution setup.", ActionUrl = "/Account/InstitutionProfile", ActionLabel = "Continue setup" });
        if (subscription == null)
        {
            items.Add(new DashboardAlert { Code = "SUBSCRIPTION_MISSING", Type = "danger",
                Message = "No active subscription found.", ActionUrl = "/Account/PlanSelection", ActionLabel = "Choose plan" });
            return items;
        }
        if (subscription.State == SubscriptionState.Expired || subscription.EndsAt <= DateTime.UtcNow)
            items.Add(new DashboardAlert { Code = "SUBSCRIPTION_EXPIRED", Type = "danger",
                Message = "Subscription has expired.", ActionUrl = "/Account/PlanSelection", ActionLabel = "Renew" });
        else if (subscription.IsTrial && trialDays <= 7)
            items.Add(new DashboardAlert { Code = "TRIAL_EXPIRING", Type = trialDays <= 3 ? "warning" : "info",
                Message = $"Trial expires in {trialDays} day(s).", Days = trialDays,
                ActionUrl = "/Account/PlanSelection", ActionLabel = "Upgrade" });
        else if (!subscription.IsTrial && expiryDays <= 7)
            items.Add(new DashboardAlert { Code = "SUBSCRIPTION_EXPIRING", Type = expiryDays <= 3 ? "danger" : "warning",
                Message = $"Subscription expires in {expiryDays} day(s).", Days = expiryDays,
                ActionUrl = "/Account/PlanSelection", ActionLabel = "Renew" });
        if (subscription.State == SubscriptionState.Grace)
            items.Add(new DashboardAlert { Code = "PAYMENT_GRACE", Type = "warning",
                Message = "Subscription is in its payment grace period.", ActionUrl = "/Account/PlanSelection",
                ActionLabel = "Review payment" });
        if (plan != null && plan.MaxStudents > 0 && studentCount * 10L >= plan.MaxStudents * 9L)
        {
            var percent = Math.Min(100, (int)(studentCount * 100L / plan.MaxStudents));
            items.Add(new DashboardAlert { Code = studentCount >= plan.MaxStudents ? "STUDENT_LIMIT_REACHED" : "STUDENT_LIMIT_WARNING",
                Type = studentCount >= plan.MaxStudents ? "danger" : "warning",
                Message = $"Student plan capacity: {studentCount}/{plan.MaxStudents}.",
                CurrentValue = studentCount, LimitValue = plan.MaxStudents, Percentage = percent,
                ActionUrl = "/Account/PlanSelection", ActionLabel = "Upgrade plan" });
        }
        return items;
    }
}
