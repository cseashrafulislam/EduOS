using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class Attendance : TenantEntity
    {
        public int StudentId { get; set; }
        public DateTime AttendanceDate { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }
    }
}
