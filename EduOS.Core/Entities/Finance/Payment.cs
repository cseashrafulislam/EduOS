using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Finance
{
    public class Payment : BaseTenantEntity
    {
        public int InvoiceId { get; set; }
        public int StudentId { get; set; }
        public string ReceiptNo { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "Cash"; // Cash/Bkash/Nagad/Card/Bank
        public DateTime PaymentDate { get; set; }
        public int ReceivedBy { get; set; }
        public string? TransactionId { get; set; }
        public string? Note { get; set; }
        public int? BankAccountId { get; set; }

        public virtual StudentInvoice? Invoice { get; set; }
        public virtual Student? Student { get; set; }
        public virtual BankAccount? BankAccount { get; set; }
    }
}
