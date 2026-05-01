using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class Group : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty; // Science/Arts/Commerce
        public string Code { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
