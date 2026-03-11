using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.LMS
{
    public class QuizResult : TenantEntity
    {
        public int QuizId { get; set; }
        public int StudentId { get; set; }
        public decimal ObtainedMarks { get; set; }
        public bool IsPassed { get; set; }
    }
}
