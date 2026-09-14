using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.LMS
{
    public class AssignmentSubmission : BaseTenantEntity
    {
        public Guid PublicId { get; set; } = Guid.NewGuid();
        public Guid ClientRequestId { get; set; }
        public long AssignmentId { get; set; }
        public long StudentId { get; set; }
        public string? SubmissionFile { get; set; }
        public string? SubmissionText { get; set; }
        public DateTime SubmittedAt { get; set; }
        public decimal? Mark { get; set; }
        public string? Feedback { get; set; }
        public string Status { get; set; } = "Submitted";
        public DateTime? ReviewedAtUtc { get; set; }
        public long? ReviewedByUserId { get; set; }
        public virtual Assignment? Assignment { get; set; }
        public virtual Student? Student { get; set; }
    }
}
