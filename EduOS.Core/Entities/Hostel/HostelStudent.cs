using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Hostel
{
    public class HostelStudent : TenantEntity
    {
        public int StudentId { get; set; }
        public int HostelRoomId { get; set; }
        public DateTime JoinDate { get; set; }
        public decimal MonthlyRent { get; set; }
    }
}
