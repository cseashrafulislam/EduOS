using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class Quiz : BaseTenantEntity
    {
        public long CourseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TotalMarks { get; set; }
        public DateTime? QuizDate { get; set; }
    }
}
