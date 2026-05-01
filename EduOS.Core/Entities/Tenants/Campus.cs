using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Tenants
{
    public class Campus : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? HeadName { get; set; }
        public bool IsHeadOffice { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }
}
