using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class Tabulation : BaseTenantEntity
    {
        public int ExamId { get; set; }
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public int StudentId { get; set; }
        public decimal TotalMarks { get; set; }
        public decimal TotalGPA { get; set; }
        public int? Position { get; set; }
        public string Result { get; set; } = "Pass"; // Pass/Fail

        public virtual Exam? Exam { get; set; }
        public virtual AcademicLevel? Class { get; set; }
        public virtual AcademicBatch? Section { get; set; }
        public virtual Student? Student { get; set; }
    }
}
