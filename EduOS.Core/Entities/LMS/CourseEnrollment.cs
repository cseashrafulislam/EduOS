using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.LMS
{
    public class CourseEnrollment : TenantEntity
    {
        public int CourseId { get; set; }
        public int StudentId { get; set; }
        public DateTime EnrollDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
