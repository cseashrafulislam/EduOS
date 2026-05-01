using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class Shift : BaseTenantEntity
    {
        public string Name { get; set; }
        public TimeSpan InTime { get; set; }
        public TimeSpan OutTime { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
