using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class QuizResult : BaseTenantEntity
    {
        public int QuizId { get; set; }
        public int StudentId { get; set; }
        public decimal ObtainedMarks { get; set; }
        public bool IsPassed { get; set; }
    }
}
