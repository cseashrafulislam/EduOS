using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Hostel
{
    public class HostelStudent : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int HostelRoomId { get; set; }
        public DateTime JoinDate { get; set; }
        public decimal MonthlyRent { get; set; }
    }
}
