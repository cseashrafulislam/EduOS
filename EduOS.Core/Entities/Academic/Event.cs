using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class Event : BaseTenantEntity
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? Location { get; set; }
        public int? OrganizerId { get; set; }
        public string Type { get; set; } = "Academic"; // Sports/Cultural/Academic
        public string Status { get; set; } = "Upcoming"; // Upcoming/Ongoing/Completed
    }
}
