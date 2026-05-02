using EduOS.Core.Enums;

namespace EduOS.Core.DTOs.SaaS
{
    /// <summary>
    /// Request to start a new subscription (during onboarding or upgrade)
    /// </summary>
    public class CreateSubscriptionRequestDto
    {
        public long SubscriptionPlanId { get; set; }
        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
        public PaymentMethod PaymentMethod { get; set; }
        public bool AutoRenew { get; set; } = true;

        /// <summary>
        /// Optional discount/coupon code
        /// </summary>
        public string? CouponCode { get; set; }
    }

    /// <summary>
    /// Response after subscription creation - contains payment instructions
    /// </summary>
    public class CreateSubscriptionResponseDto
    {
        public long SubscriptionId { get; set; }
        public long? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "BDT";

        public SubscriptionStatus Status { get; set; }
        public bool IsTrialActivated { get; set; }
        public DateTime? TrialEndsAt { get; set; }

        /// <summary>
        /// For online payment: gateway redirect URL
        /// </summary>
        public string? PaymentRedirectUrl { get; set; }

        /// <summary>
        /// For manual payment: bank account info to deposit to
        /// </summary>
        public ManualPaymentInstructionsDto? ManualPaymentInstructions { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    public class ManualPaymentInstructionsDto
    {
        public string BankName { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string? RoutingNumber { get; set; }
        public string? BranchName { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
    }

    /// <summary>
    /// Current active subscription details for the tenant
    /// </summary>
    public class CurrentSubscriptionDto
    {
        public long Id { get; set; }
        public long PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public string PlanCode { get; set; } = string.Empty;

        public BillingCycle BillingCycle { get; set; }
        public SubscriptionStatus Status { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime? NextBillingDate { get; set; }

        public bool IsTrial { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public int? TrialDaysRemaining { get; set; }

        public decimal Price { get; set; }
        public decimal FinalAmount { get; set; }
        public string Currency { get; set; } = "BDT";

        public bool AutoRenew { get; set; }
        public bool CancelAtPeriodEnd { get; set; }

        // Usage vs limits
        public int MaxStudents { get; set; }
        public int CurrentStudents { get; set; }
        public int MaxTeachers { get; set; }
        public int CurrentTeachers { get; set; }
        public int MaxCampuses { get; set; }
        public int CurrentCampuses { get; set; }

        public int DaysRemaining { get; set; }
    }

    /// <summary>
    /// Subscription history list item
    /// </summary>
    public class SubscriptionHistoryDto
    {
        public long Id { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public BillingCycle BillingCycle { get; set; }
        public SubscriptionStatus Status { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal FinalAmount { get; set; }
        public string Currency { get; set; } = "BDT";
    }
}
