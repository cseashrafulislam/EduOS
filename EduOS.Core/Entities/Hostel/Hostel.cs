using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Hostel
{
    public class Hostel : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Boys"; // Boys/Girls
        public int TotalRooms { get; set; }
        public string? WardenName { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual ICollection<HostelRoom> Rooms { get; set; } = new List<HostelRoom>();
    }
}
