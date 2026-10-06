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
        public int OnboardingStep { get; set; }
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
