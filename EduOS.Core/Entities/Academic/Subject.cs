using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class Subject : TenantEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int? ClassId { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
