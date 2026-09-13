using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class SeatPlan : BaseTenantEntity
    {
        public long ExamScheduleId { get; set; }
        public long StudentId { get; set; }
        public long HallId { get; set; }
        public string SeatNo { get; set; } = string.Empty;

        public virtual ExamSchedule? ExamSchedule { get; set; }
        public virtual Student? Student { get; set; }
        public virtual ExamHall? Hall { get; set; }
    }
}
