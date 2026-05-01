using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Hostel
{
    public class HostelRoom : BaseTenantEntity
    {
        public int HostelId { get; set; }
        public string RoomNo { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public int AvailableBeds { get; set; }
        public decimal RentPerBed { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Hostel? Hostel { get; set; }
    }
}
