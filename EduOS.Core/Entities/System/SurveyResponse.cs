using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class SurveyResponse : BaseEntity
    {
        public int SurveyId { get; set; }
        public int QuestionId { get; set; }
        public int? RespondentId { get; set; }
        public string? Response { get; set; }
        public DateTime SubmittedAt { get; set; }

        public virtual Survey? Survey { get; set; }
        public virtual SurveyQuestion? Question { get; set; }
    }
}
