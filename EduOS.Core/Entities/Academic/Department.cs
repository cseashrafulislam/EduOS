using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class Department : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? HeadOfDepartment { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
