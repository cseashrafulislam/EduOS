using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Finance
{
    public class FeeType : TenantEntity
    {
        public string Name { get; set; }
        public decimal DefaultAmount { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
