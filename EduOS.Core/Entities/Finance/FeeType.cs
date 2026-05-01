using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class FeeType : BaseTenantEntity
    {
        public string Name { get; set; }
        public decimal DefaultAmount { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
