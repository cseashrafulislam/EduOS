using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class ExamMark : TenantEntity
    {
        public int ExamId { get; set; }
        public int StudentId { get; set; }
        public int SubjectId { get; set; }
        public decimal ObtainedMarks { get; set; }
        public decimal TotalMarks { get; set; }
        public string Grade { get; set; }
    }
}
