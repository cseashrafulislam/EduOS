using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class IncomeCategory : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
