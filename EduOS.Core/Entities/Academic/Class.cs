using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class Class : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty; // Class One/Two
        public int NumericValue { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual ICollection<Section> Sections { get; set; } = new List<Section>();
        public virtual ICollection<Subject> Subjects { get; set; } = new List<Subject>();
    }
}
