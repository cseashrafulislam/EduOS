using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory
{
    public class AssetMaintenance : BaseTenantEntity
    {
        public int AssetId { get; set; }
        public DateTime MaintenanceDate { get; set; }
        public string MaintenanceType { get; set; } = "Repair"; // Repair/Service
        public decimal Cost { get; set; }
        public string? Description { get; set; }
        public string? PerformedBy { get; set; }
        public DateTime? NextDueDate { get; set; }

        public virtual Asset? Asset { get; set; }
    }
}
