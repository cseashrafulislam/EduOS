using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class AcademicYear : TenantEntity
    {
        public string Name { get; set; } = string.Empty; // 2026, 2026-2027
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsCurrent { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }
}