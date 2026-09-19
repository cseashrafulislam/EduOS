using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Hostel
{
    public class HostelStudent : BaseTenantEntity
    {
        public long StudentId { get; set; }
        public long HostelRoomId { get; set; }
        public DateTime JoinDate { get; set; }
        public decimal MonthlyRent { get; set; }
    }
}
