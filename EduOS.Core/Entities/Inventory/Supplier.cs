using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Inventory
{
    public class Supplier : TenantEntity
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
    }
}
