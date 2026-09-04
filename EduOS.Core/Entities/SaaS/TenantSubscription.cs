using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.SaaS
{
    /// <summary>
    /// Represents an actual subscription instance for a Tenant.
    /// One Tenant can have multiple subscription records over time
    /// (history of plan changes, renewals, etc.) but only ONE Active at a time.
    /// </summary>
    public class TenantSubscription : BaseEntity
    {
        public long TenantId { get; set; }
        public long SubscriptionPlanId { get; set; }

        // ==================== Period ====================

        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>
        /// When the next invoice will be auto-generated (for auto-renew)
        /// </summary>
        public DateTime? NextBillingDate { get; set; }

        // ==================== Trial ====================

        public bool IsTrial { get; set; }
        public DateTime? TrialStartDate { get; set; }
        public DateTime? TrialEndDate { get; set; }

        // ==================== Pricing snapshot (price at time of subscription) ====================

        /// <summary>
        /// Locked-in price (plans may change later but tenant keeps this rate)
        /// </summary>
        public decimal Price { get; set; }

        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal FinalAmount { get; set; }

        public string Currency { get; set; } = "BDT";

        // ==================== Status ====================

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.PendingPayment;

        public bool AutoRenew { get; set; } = true;

        /// <summary>
        /// User requested cancellation at period end
        /// </summary>
        public bool CancelAtPeriodEnd { get; set; }

        public DateTime? CancelledAt { get; set; }
        public string? CancellationReason { get; set; }

        // ==================== Limits snapshot (cached for performance) ====================

        public int MaxStudents { get; set; }
        public int MaxTeachers { get; set; }
        public int MaxCampuses { get; set; }
        public int MaxStorageMb { get; set; }

        // ==================== Navigation ====================

        public virtual Tenant? Tenant { get; set; }
        public virtual SubscriptionPlan? SubscriptionPlan { get; set; }
        public virtual ICollection<SubscriptionInvoice> Invoices { get; set; } = new List<SubscriptionInvoice>();
    }
}
