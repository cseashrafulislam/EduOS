using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class AcademicYear : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty; // 2026
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsCurrent { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }
}
