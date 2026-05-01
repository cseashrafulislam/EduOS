using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Fine : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Type { get; set; } = "Late"; // Late/Damage/Other
        public bool IsActive { get; set; } = true;
    }
}
