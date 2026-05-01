using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    public class SubscriptionInvoice : BaseEntity
    {
        public int TenantId { get; set; }
        public int SubscriptionId { get; set; }
        public string InvoiceNo { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string BillingMonth { get; set; } = string.Empty;
        public int BillingYear { get; set; }
        public DateTime DueDate { get; set; }
        public string PaymentStatus { get; set; } = "Unpaid"; // Paid/Unpaid/Overdue
        public DateTime? PaymentDate { get; set; }
        public string? PaymentMethod { get; set; }
        public string? TransactionId { get; set; }

        public virtual TenantSubscription? Subscription { get; set; }
    }
}
