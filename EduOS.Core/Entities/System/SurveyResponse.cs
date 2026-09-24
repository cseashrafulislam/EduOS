using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class SurveyResponse : BaseEntity
    {
        public long SurveyId { get; set; }
        public long QuestionId { get; set; }
        public long? RespondentId { get; set; }
        public string? Response { get; set; }
        public DateTime SubmittedAt { get; set; }

        public virtual Survey? Survey { get; set; }
        public virtual SurveyQuestion? Question { get; set; }
    }
}
