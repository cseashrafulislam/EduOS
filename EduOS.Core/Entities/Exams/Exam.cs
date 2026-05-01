using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Exams
{
    public class Exam : BaseTenantEntity
    {
        public int AcademicYearId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // ClassTest/Mid/Final
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int WeightPercentage { get; set; }
        public bool IsPublished { get; set; } = false;
        public bool IsActive { get; set; } = true;

        public virtual AcademicYear? AcademicYear { get; set; }
    }
}
