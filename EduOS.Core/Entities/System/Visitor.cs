using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Visitor : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? NID { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string? ToMeet { get; set; }
        public int? MeetingPersonId { get; set; }
        public DateTime InTime { get; set; }
        public DateTime? OutTime { get; set; }
        public string? PhotoUrl { get; set; }
        public string? BadgeNo { get; set; }
    }
}
