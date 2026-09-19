using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class QuizResult : BaseTenantEntity
    {
        public long QuizId { get; set; }
        public long StudentId { get; set; }
        public decimal ObtainedMarks { get; set; }
        public bool IsPassed { get; set; }
    }
}
