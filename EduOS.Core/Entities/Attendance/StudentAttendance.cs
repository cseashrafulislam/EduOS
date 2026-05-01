using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Attendance
{
    public class StudentAttendance : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public DateTime Date { get; set; }
        public string Status { get; set; } = string.Empty; // Present/Absent/Late/Leave
        public TimeSpan? InTime { get; set; }
        public TimeSpan? OutTime { get; set; }
        public string? Remarks { get; set; }
        public int MarkedBy { get; set; }

        public virtual Student? Student { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Section? Section { get; set; }
    }
}
