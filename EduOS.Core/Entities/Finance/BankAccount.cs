using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class BankAccount : BaseTenantEntity
    {
        public string AccountName { get; set; } = string.Empty;
        public string? AccountNo { get; set; }
        public string? BankName { get; set; }
        public string? Branch { get; set; }
        public string AccountType { get; set; } = "Cash"; // Cash/Bank/Mobile
        public decimal OpeningBalance { get; set; }
        public decimal CurrentBalance { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
