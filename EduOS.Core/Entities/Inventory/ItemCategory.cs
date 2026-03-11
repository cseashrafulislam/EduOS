using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Inventory
{
    public class ItemCategory : TenantEntity
    {
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
