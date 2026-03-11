using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.HR
{
    public class Designation : TenantEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
