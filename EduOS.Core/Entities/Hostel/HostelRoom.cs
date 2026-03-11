using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Hostel
{
    public class HostelRoom : TenantEntity
    {
        public int HostelId { get; set; }
        public string RoomNo { get; set; }
        public int SeatCapacity { get; set; }
        public decimal SeatRent { get; set; }
    }
}
