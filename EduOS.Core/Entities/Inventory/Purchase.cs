using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Inventory
{
    public class Purchase : TenantEntity
    {
        public string PurchaseNo { get; set; }
        public int SupplierId { get; set; }
        public DateTime PurchaseDate { get; set; }
        public decimal TotalAmount { get; set; }
    }
}
