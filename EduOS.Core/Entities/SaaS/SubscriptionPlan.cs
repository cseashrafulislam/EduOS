using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.SaaS
{
    /// <summary>
    /// Represents a subscription tier offered by the EduOS platform.
    /// Examples: Free Trial, Basic, Pro, Enterprise.
    /// Managed only by SuperAdmin.
    /// </summary>
    public class SubscriptionPlan : BaseEntity
    {
        // ==================== Identification ====================

        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty; // e.g. "BASIC", "PRO"
        public string? Description { get; set; }
        public string? ShortDescription { get; set; }
        public string? IconUrl { get; set; }

        /// <summary>
        /// Display order on pricing page (lower = first)
        /// </summary>
        public int DisplayOrder { get; set; }

        /// <summary>
        /// Highlight as recommended on pricing page
        /// </summary>
        public bool IsRecommended { get; set; }

        /// <summary>
        /// True for the free trial plan (no payment required)
        /// </summary>
        public bool IsFreeTrial { get; set; }

        /// <summary>
        /// Trial duration in days (only used when IsFreeTrial = true)
        /// </summary>
        public int? TrialDays { get; set; }

        // ==================== Pricing ====================

        /// <summary>
        /// Monthly price (BDT). Use 0 for free plans.
        /// </summary>
        public decimal MonthlyPrice { get; set; }

        /// <summary>
        /// Quarterly price (3 months). Usually with discount.
        /// </summary>
        public decimal QuarterlyPrice { get; set; }

        /// <summary>
        /// Half-yearly price (6 months). Usually with discount.
        /// </summary>
        public decimal HalfYearlyPrice { get; set; }

        /// <summary>
        /// Yearly price. Usually with biggest discount.
        /// </summary>
        public decimal YearlyPrice { get; set; }

        /// <summary>
        /// One-time setup fee (optional)
        /// </summary>
        public decimal SetupFee { get; set; }

        public string Currency { get; set; } = "BDT";

        // ==================== Limits ====================

        public int MaxStudents { get; set; }
        public int MaxTeachers { get; set; }
        public int MaxCampuses { get; set; } = 1;
        public int MaxAdminUsers { get; set; } = 5;
        public int MaxStorageMb { get; set; } = 1024;
        public int MaxSmsPerMonth { get; set; }
        public int MaxEmailsPerMonth { get; set; }

        // ==================== Status ====================

        public bool IsActive { get; set; } = true;
        public bool IsPubliclyVisible { get; set; } = true;

        // ==================== Navigation ====================

        public virtual ICollection<PlanFeature> PlanFeatures { get; set; } = new List<PlanFeature>();
        public virtual ICollection<TenantSubscription> Subscriptions { get; set; } = new List<TenantSubscription>();
    }
}
