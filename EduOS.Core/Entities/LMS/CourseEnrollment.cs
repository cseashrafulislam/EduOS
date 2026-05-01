using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class CourseEnrollment : BaseTenantEntity
    {
        public int CourseId { get; set; }
        public int StudentId { get; set; }
        public DateTime EnrollDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
