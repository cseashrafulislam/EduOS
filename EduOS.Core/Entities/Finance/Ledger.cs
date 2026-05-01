using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Ledger : BaseTenantEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public string Type { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
