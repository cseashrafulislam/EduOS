using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class Subject : BaseTenantEntity
    {
        public int ClassId { get; set; }
        public int? GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int FullMark { get; set; } = 100;
        public int PassMark { get; set; } = 33;
        public bool IsOptional { get; set; } = false;
        public bool IsActive { get; set; } = true;

        public virtual Class? Class { get; set; }
        public virtual Group? Group { get; set; }
    }
}
