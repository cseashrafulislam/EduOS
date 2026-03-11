using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.HR
{
    public class Shift : TenantEntity
    {
        public string Name { get; set; }
        public TimeSpan InTime { get; set; }
        public TimeSpan OutTime { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
