using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Account : BaseTenantEntity
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty; // Asset/Liability/Income/Expense/Equity
        public int? ParentAccountId { get; set; }
        public decimal OpeningBalance { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Account? ParentAccount { get; set; }
        public virtual ICollection<Account> SubAccounts { get; set; } = new List<Account>();
    }
}
