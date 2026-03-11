using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class ExamSchedule : TenantEntity
    {
        public int ExamId { get; set; }
        public int SubjectId { get; set; }
        public DateTime ExamDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }
}
