using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.LMS
{
    public class Course : BaseTenantEntity
    {
        public Guid PublicId { get; set; } = Guid.NewGuid();
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public long? SectionId { get; set; }
        public long SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long TeacherId { get; set; }
        public string? ThumbnailUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Section? Section { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual Employee? Teacher { get; set; }
        public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
        public virtual ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
        public virtual ICollection<CourseEnrollment> Enrollments { get; set; } = new List<CourseEnrollment>();
    }
}
