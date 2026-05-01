using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.LMS
{
    public class AssignmentSubmission : BaseTenantEntity
    {
        public int AssignmentId { get; set; }
        public int StudentId { get; set; }
        public string? SubmissionFile { get; set; }
        public string? SubmissionText { get; set; }
        public DateTime SubmittedAt { get; set; }
        public decimal? Mark { get; set; }
        public string? Feedback { get; set; }
        public string Status { get; set; } = "Submitted"; // Submitted/Reviewed

        public virtual Assignment? Assignment { get; set; }
        public virtual Student? Student { get; set; }
    }
}
