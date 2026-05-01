using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Income : BaseTenantEntity
    {
        public int CategoryId { get; set; }
        public int BankAccountId { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }
        public string? ReceiptNo { get; set; }
        public string? ReceivedFrom { get; set; }
        public int AddedBy { get; set; }

        public virtual IncomeCategory? Category { get; set; }
        public virtual BankAccount? BankAccount { get; set; }
    }
}
