using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class ExamResult : BaseTenantEntity
    {
        public long ExamId { get; set; }
        public long StudentId { get; set; }
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public long SectionId { get; set; }
        public long? GroupId { get; set; }
        public decimal TotalMark { get; set; }
        public decimal TotalFullMark { get; set; }
        public decimal Percentage { get; set; }
        public decimal TotalGPA { get; set; }
        public string? FinalGrade { get; set; }
        public int? Position { get; set; }
        public bool IsPassed { get; set; } = true;
        public bool IsPublished { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public long? PublishedByUserId { get; set; }

        public virtual Exam? Exam { get; set; }
        public virtual Student? Student { get; set; }
    }
}
