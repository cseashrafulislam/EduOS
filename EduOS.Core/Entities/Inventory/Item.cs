using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Inventory
{
    public class Item : TenantEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int? CategoryId { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal SalesPrice { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
