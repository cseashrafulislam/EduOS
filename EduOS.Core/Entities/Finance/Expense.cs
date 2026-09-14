using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Expense : BaseTenantEntity
    {
        public long CategoryId { get; set; }
        public long BankAccountId { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? Description { get; set; }
        public string? VoucherNo { get; set; }
        public string? PaidTo { get; set; }
        public long AddedBy { get; set; }

        public virtual ExpenseCategory? Category { get; set; }
        public virtual BankAccount? BankAccount { get; set; }
    }
}
