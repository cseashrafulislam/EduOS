using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class Section : BaseTenantEntity
    {
        public long ClassId { get; set; }
        public string Name { get; set; } = string.Empty; // A/B/C
        public int Capacity { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Class? Class { get; set; }
    }
}
