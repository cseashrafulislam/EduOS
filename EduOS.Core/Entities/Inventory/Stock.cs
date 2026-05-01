using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory
{
    public class Stock : BaseTenantEntity
    {
        public int ItemId { get; set; }
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal BalanceQty { get; set; }
    }
}
