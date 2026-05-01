using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class ExamResult : BaseTenantEntity
    {
        public int ExamId { get; set; }
        public int StudentId { get; set; }
        public decimal TotalMark { get; set; }
        public decimal TotalGPA { get; set; }
        public string? FinalGrade { get; set; }
        public int? Position { get; set; }
        public bool IsPassed { get; set; } = true;

        public virtual Exam? Exam { get; set; }
        public virtual Student? Student { get; set; }
    }
}
