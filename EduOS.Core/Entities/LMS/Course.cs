using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.LMS
{
    public class Course : BaseTenantEntity
    {
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int TeacherId { get; set; }
        public string? ThumbnailUrl { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Class? Class { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual Employee? Teacher { get; set; }
        public virtual ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    }
}
