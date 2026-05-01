using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Exams
{
    public class SeatPlan : BaseTenantEntity
    {
        public int ExamScheduleId { get; set; }
        public int StudentId { get; set; }
        public int HallId { get; set; }
        public string SeatNo { get; set; } = string.Empty;

        public virtual ExamSchedule? ExamSchedule { get; set; }
        public virtual Student? Student { get; set; }
        public virtual ExamHall? Hall { get; set; }
    }
}
