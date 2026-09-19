using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Academic
{
    public class SubjectTeacher : BaseTenantEntity
    {
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public long SectionId { get; set; }
        public long SubjectId { get; set; }
        public long TeacherId { get; set; }
        public bool IsClassTeacher { get; set; } = false;

        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Section? Section { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual Employee? Teacher { get; set; }
    }
}
