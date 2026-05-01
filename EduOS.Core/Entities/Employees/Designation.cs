using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Employees
{
    public class Designation : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public int Rank { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
