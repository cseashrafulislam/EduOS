using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class MarkEntry : BaseTenantEntity
    {
        public int ExamId { get; set; }
        public int StudentId { get; set; }
        public int SubjectId { get; set; }
        public decimal ObtainedMark { get; set; }
        public int FullMark { get; set; }
        public bool IsAbsent { get; set; } = false;
        public string? Grade { get; set; }
        public decimal? GPA { get; set; }
        public int EnteredBy { get; set; }
        public DateTime EntryDate { get; set; }

        public virtual Exam? Exam { get; set; }
        public virtual Student? Student { get; set; }
        public virtual Subject? Subject { get; set; }
    }
}
