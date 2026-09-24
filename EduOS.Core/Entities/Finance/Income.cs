using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Income : BaseTenantEntity
    {
        public long CategoryId { get; set; }
        public long BankAccountId { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }
        public string? ReceiptNo { get; set; }
        public string? ReceivedFrom { get; set; }
        public long AddedBy { get; set; }

        public virtual IncomeCategory? Category { get; set; }
        public virtual BankAccount? BankAccount { get; set; }
    }
}
