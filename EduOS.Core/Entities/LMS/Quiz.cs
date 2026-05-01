using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class Quiz : BaseTenantEntity
    {
        public int CourseId { get; set; }
        public string Title { get; set; }
        public int TotalMarks { get; set; }
        public DateTime? QuizDate { get; set; }
    }
}
