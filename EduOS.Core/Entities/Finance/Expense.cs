using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Finance
{
    public class Expense : TenantEntity
    {
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string Note { get; set; }
    }
}
