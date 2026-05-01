using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Tenants
{
    public class Shift : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty; // Morning/Day/Evening
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
