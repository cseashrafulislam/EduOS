using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class Designation : BaseTenantEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
