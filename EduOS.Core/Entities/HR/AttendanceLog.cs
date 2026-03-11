using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.HR
{
    public class AttendanceLog : TenantEntity
    {
        public int EmployeeId { get; set; }
        public DateTime AttendanceDate { get; set; }
        public TimeSpan? InTime { get; set; }
        public TimeSpan? OutTime { get; set; }
        public string Status { get; set; }
    }
}
