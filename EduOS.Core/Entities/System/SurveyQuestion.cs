using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class SurveyQuestion : BaseEntity
    {
        public int SurveyId { get; set; }
        public string Question { get; set; } = string.Empty;
        public string QuestionType { get; set; } = "Rating"; // Rating/MCQ/Text
        public string? Options { get; set; } // JSON
        public bool IsRequired { get; set; } = true;
        public int OrderNo { get; set; }

        public virtual Survey? Survey { get; set; }
    }
}
