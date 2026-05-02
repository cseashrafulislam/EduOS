using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.SaaS
{
    /// <summary>
    /// Invoice for a tenant's subscription billing cycle.
    /// Generated automatically when a subscription period starts.
    /// </summary>
    public class SubscriptionInvoice : BaseEntity
    {
        public long TenantId { get; set; }
        public long TenantSubscriptionId { get; set; }

        // ==================== Invoice Identification ====================

        public string InvoiceNumber { get; set; } = string.Empty; // e.g. "INV-202605-00001"

        public DateTime IssueDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // ==================== Amounts ====================

        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal DueAmount { get; set; }

        public string Currency { get; set; } = "BDT";

        // ==================== Status ====================

        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

        public DateTime? PaidAt { get; set; }

        // ==================== Customer snapshot (for invoice display) ====================

        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerAddress { get; set; }

        // ==================== Notes ====================

        public string? Description { get; set; }
        public string? InternalNote { get; set; }

        // ==================== Navigation ====================

        public virtual Tenant? Tenant { get; set; }
        public virtual TenantSubscription? Subscription { get; set; }
        public virtual ICollection<SubscriptionPayment> Payments { get; set; } = new List<SubscriptionPayment>();
    }
}
