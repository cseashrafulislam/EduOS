using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.HR
{
    public class AttendanceLog : BaseTenantEntity
    {
        public int EmployeeId { get; set; }
        public DateTime AttendanceDate { get; set; }
        public TimeSpan? InTime { get; set; }
        public TimeSpan? OutTime { get; set; }
        public string Status { get; set; }
    }
}
