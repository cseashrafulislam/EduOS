using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.LMS
{
    public class Course : TenantEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int TeacherId { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
