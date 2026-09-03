using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.SaaS
{
    /// <summary>
    /// Records each payment transaction made against a SubscriptionInvoice.
    /// One invoice can have multiple payments (partial payments, retries, etc.).
    /// </summary>
    public class SubscriptionPayment : BaseEntity, ITenantScopedEntity
    {
        public long TenantId { get; set; }
        public long SubscriptionInvoiceId { get; set; }

        // ==================== Transaction Identification ====================

        public string TransactionId { get; set; } = string.Empty; // Internal ID
        public string? GatewayTransactionId { get; set; }         // From AamarPay/etc.
        public string? GatewayReference { get; set; }

        // ==================== Payment Details ====================

        public PaymentMethod PaymentMethod { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "BDT";

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime? FailedAt { get; set; }

        // ==================== Manual Payment (Bank Transfer) ====================

        /// <summary>
        /// For manual payments - bank account info, deposit slip number, etc.
        /// </summary>
        public string? PayerBankName { get; set; }
        public string? PayerAccountNumber { get; set; }
        public string? DepositSlipNumber { get; set; }
        public DateTime? DepositDate { get; set; }
        public string? DepositSlipUrl { get; set; }

        /// <summary>
        /// Admin verifies manual payment
        /// </summary>
        public long? VerifiedByUserId { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? VerificationNote { get; set; }

        // ==================== Gateway Response ====================

        /// <summary>
        /// Raw JSON response from gateway for debugging
        /// </summary>
        public string? GatewayResponse { get; set; }

        public string? FailureReason { get; set; }

        // ==================== Navigation ====================

        public virtual Tenant? Tenant { get; set; }
        public virtual SubscriptionInvoice? Invoice { get; set; }
    }
}
