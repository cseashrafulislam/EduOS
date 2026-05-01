using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Hostel
{
    public class StudentHostel : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int HostelId { get; set; }
        public int HostelRoomId { get; set; }
        public string? BedNo { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal MonthlyRent { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Student? Student { get; set; }
        public virtual Hostel? Hostel { get; set; }
        public virtual HostelRoom? HostelRoom { get; set; }
    }
}
