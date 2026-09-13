using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.LMS
{
    public class CourseEnrollment : BaseTenantEntity
    {
        public long CourseId { get; set; }
        public long StudentId { get; set; }
        public DateTime EnrollDate { get; set; }
        public bool IsActive { get; set; } = true;
        public decimal ProgressPercentage { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public virtual Course? Course { get; set; }
        public virtual Student? Student { get; set; }
        public virtual ICollection<LessonProgress> LessonProgress { get; set; } = new List<LessonProgress>();
    }
}
