using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Finance
{
    public class Payment : BaseTenantEntity
    {
        public Guid PublicId { get; set; } = Guid.NewGuid();
        public Guid ClientRequestId { get; set; }
        public long InvoiceId { get; set; }
        public long StudentId { get; set; }
        public string ReceiptNo { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public DateTime PaymentDate { get; set; }
        public long ReceivedBy { get; set; }
        public string? TransactionId { get; set; }
        public string? Note { get; set; }
        public long? BankAccountId { get; set; }
        public virtual StudentInvoice? Invoice { get; set; }
        public virtual Student? Student { get; set; }
        public virtual BankAccount? BankAccount { get; set; }
    }
}
