using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory
{
    public class Asset : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public DateTime PurchaseDate { get; set; }
        public string Condition { get; set; } = "New"; // New/Good/Damaged
        public string? Location { get; set; }
        public string? SerialNo { get; set; }
        public string? Description { get; set; }
    }
}
