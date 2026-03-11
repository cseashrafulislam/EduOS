using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class TeacherAttendance : TenantEntity
    {
        public int TeacherId { get; set; }
        public DateTime AttendanceDate { get; set; }
        public string Status { get; set; }
    }
}
