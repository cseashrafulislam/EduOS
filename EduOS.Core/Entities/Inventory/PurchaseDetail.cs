using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory
{
    public class PurchaseDetail : BaseTenantEntity
    {
        public int PurchaseId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
