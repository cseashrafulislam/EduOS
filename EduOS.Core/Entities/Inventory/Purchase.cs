using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory
{
    public class Purchase : BaseTenantEntity
    {
        public string PurchaseNo { get; set; }
        public int SupplierId { get; set; }
        public DateTime PurchaseDate { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
