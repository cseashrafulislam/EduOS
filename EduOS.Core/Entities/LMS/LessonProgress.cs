using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.LMS;

public class LessonProgress : BaseTenantEntity
{
    public long CourseEnrollmentId { get; set; }
    public long LessonId { get; set; }
    public long StudentId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public virtual CourseEnrollment? CourseEnrollment { get; set; }
    public virtual Lesson? Lesson { get; set; }
    public virtual Student? Student { get; set; }
}
