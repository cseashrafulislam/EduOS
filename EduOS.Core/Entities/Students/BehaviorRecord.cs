using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class BehaviorRecord : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public DateTime Date { get; set; }
        public string Type { get; set; } = "Positive"; // Positive/Negative
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Action { get; set; }
        public int ReportedBy { get; set; }
        public bool ParentNotified { get; set; } = false;

        public virtual Student? Student { get; set; }
    }
}
