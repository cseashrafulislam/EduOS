using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Exams
{
    public class Question : BaseTenantEntity
    {
        public int SubjectId { get; set; }
        public int? ChapterId { get; set; }
        public string Type { get; set; } = "MCQ"; // MCQ/Short/Broad
        public string QuestionText { get; set; } = string.Empty;
        public decimal Mark { get; set; }
        public string Difficulty { get; set; } = "Medium"; // Easy/Medium/Hard
        public string? CorrectAnswer { get; set; }
        public string? Options { get; set; } // JSON for MCQ

        public virtual Subject? Subject { get; set; }
    }
}
