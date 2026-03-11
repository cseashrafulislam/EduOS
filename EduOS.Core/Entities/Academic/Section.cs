using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class Section : TenantEntity
    {
        public string Name { get; set; }
        public int ClassId { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
