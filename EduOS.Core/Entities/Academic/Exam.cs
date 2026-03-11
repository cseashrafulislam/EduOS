using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class Exam : TenantEntity
    {
        public string Name { get; set; }
        public int? ClassId { get; set; }
        public int? AcademicYearId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsPublished { get; set; }
    }
}
