using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class FeeHead : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Monthly"; // Monthly/OneTime/Annual
        public bool IsActive { get; set; } = true;
    }
}
