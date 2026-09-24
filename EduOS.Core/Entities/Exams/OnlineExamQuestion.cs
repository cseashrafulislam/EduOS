using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Exams
{
    public class OnlineExamQuestion : BaseEntity
    {
        public long OnlineExamId { get; set; }
        public long QuestionId { get; set; }
        public int QuestionOrder { get; set; }

        public virtual OnlineExam? OnlineExam { get; set; }
        public virtual Question? Question { get; set; }
    }
}
