using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Attendance
{
    public class EmployeeAttendance : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public DateTime Date { get; set; }
        public string Status { get; set; } = string.Empty; // Present/Absent/Late/Leave/Holiday
        public TimeSpan? InTime { get; set; }
        public TimeSpan? OutTime { get; set; }
        public decimal? OvertimeHours { get; set; }
        public string? Remarks { get; set; }

        public virtual Employee? Employee { get; set; }
    }
}
