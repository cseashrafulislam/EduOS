using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class HRDesignation : BaseTenantEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
