using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Exams
{
    public class ExamSchedule : BaseTenantEntity
    {
        public int ExamId { get; set; }
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public DateTime ExamDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int FullMark { get; set; } = 100;
        public int PassMark { get; set; } = 33;
        public string? RoomNo { get; set; }

        public virtual Exam? Exam { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Subject? Subject { get; set; }
    }
}
