using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory
{
    public class ItemCategory : BaseTenantEntity
    {
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
