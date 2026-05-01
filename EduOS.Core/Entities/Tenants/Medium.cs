using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Tenants
{
    public class Medium : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty; // Bangla Version/English Version/Madrasha
        public string Code { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
