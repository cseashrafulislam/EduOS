using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Complaint : BaseTenantEntity
    {
        public long? SubmittedBy { get; set; } // null = Anonymous
        public string Type { get; set; } = "Complaint"; // Complaint/Suggestion
        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Open"; // Open/InProgress/Resolved
        public long? AssignedTo { get; set; }
        public string? Resolution { get; set; }
        public DateTime SubmittedAt { get; set; }
    }
}
