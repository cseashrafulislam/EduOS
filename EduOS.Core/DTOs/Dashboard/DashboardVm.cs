namespace EduOS.Core.DTOs.Dashboard
{
    public class DashboardVm
    {
        // ── Institution Info ───────────────────────────────────
        public string InstitutionName { get; set; } = string.Empty;
        public string? InstitutionType { get; set; }
        public string? OwnerName { get; set; }
        public string? LogoUrl { get; set; }

        // ── Subscription ───────────────────────────────────────
        public string PlanName { get; set; } = "N/A";
        public string? PlanNameBangla { get; set; }
        public string PlanCode { get; set; } = string.Empty;
        public bool IsTrialActive { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public int? TrialDaysRemaining { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }
        public int DaysUntilExpiry { get; set; }
        public string SubscriptionStatus { get; set; } = string.Empty;

        // ── Onboarding ─────────────────────────────────────────
        public bool EmailVerified { get; set; }
        public bool OnboardingComplete { get; set; }
        public EduOS.Core.Enums.Domain.OnboardingStage OnboardingStage { get; set; }
        public int OnboardingPercent { get; set; }

        // ── Limits & Usage ─────────────────────────────────────
        public int MaxStudents { get; set; }
        public int CurrentStudents { get; set; }
        public int MaxTeachers { get; set; }
        public int CurrentTeachers { get; set; }
        public int MaxCampuses { get; set; }
        public int ActiveFeatures { get; set; }

        // ── Stats (for dashboard widgets) ──────────────────────
        public int TotalStudents { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalStaff { get; set; }
        public int TotalCampuses { get; set; }
        public int TotalClasses { get; set; }
        public decimal MonthlyCollection { get; set; }
        public decimal TotalDues { get; set; }

        // ── Alerts (shown as banners on dashboard) ─────────────
        public List<DashboardAlert> Alerts { get; set; } = new();
    }

    public class DashboardAlert
    {
        /// <summary>
        /// Stable machine-readable code used by clients to localize the alert.
        /// Message remains populated for backward compatibility with API clients.
        /// </summary>
        public string Code { get; set; } = string.Empty;
        public string Type { get; set; } = "info";   // info | warning | danger | success
        public string Message { get; set; } = string.Empty;
        public string? ActionCode { get; set; }
        public string? ActionUrl { get; set; }
        public string? ActionLabel { get; set; }
        public int? Days { get; set; }
        public int? Percentage { get; set; }
        public int? CurrentValue { get; set; }
        public int? LimitValue { get; set; }
    }
}

namespace EduOS.Core.DTOs.Dashboard
{
    /// <summary>Read-only, platform-wide operational summary; never includes tenant-private records.</summary>
    public sealed class PlatformDashboardVm
    {
        public DateTime GeneratedAtUtc { get; set; }
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
        public int PendingVerificationTenants { get; set; }
        public int IncompleteOnboardingTenants { get; set; }
        public int NewTenantsLast30Days { get; set; }
        public int SubscriptionsExpiringNext7Days { get; set; }
        public int OverdueActiveSubscriptions { get; set; }
        public int? PendingManualPayments { get; set; }
        public List<PlatformTenantSummaryVm> RecentTenants { get; set; } = new();
        public List<PlatformSubscriptionExpiryVm> UpcomingExpirations { get; set; } = new();
    }
    public sealed class PlatformTenantSummaryVm
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string OnboardingStage { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
    public sealed class PlatformSubscriptionExpiryVm
    {
        public long SubscriptionId { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string SubscriptionState { get; set; } = string.Empty;
        public DateTime EndsAtUtc { get; set; }
    }
}
