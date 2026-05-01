using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class OnlineExamAttempt : BaseEntity
    {
        public int OnlineExamId { get; set; }
        public int StudentId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public decimal TotalScore { get; set; }
        public string Status { get; set; } = "InProgress"; // InProgress/Submitted

        public virtual OnlineExam? OnlineExam { get; set; }
        public virtual Student? Student { get; set; }
    }
}
