using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Inventory
{
    public class PurchaseDetail : TenantEntity
    {
        public int PurchaseId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
