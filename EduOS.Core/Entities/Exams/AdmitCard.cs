using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class AdmitCard : BaseTenantEntity
    {
        public int ExamId { get; set; }
        public int StudentId { get; set; }
        public string AdmitNo { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public bool IsIssued { get; set; } = false;

        public virtual Exam? Exam { get; set; }
        public virtual Student? Student { get; set; }
    }
}
