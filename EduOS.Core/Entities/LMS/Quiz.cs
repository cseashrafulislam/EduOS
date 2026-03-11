using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.LMS
{
    public class Quiz : TenantEntity
    {
        public int CourseId { get; set; }
        public string Title { get; set; }
        public int TotalMarks { get; set; }
        public DateTime? QuizDate { get; set; }
    }
}
