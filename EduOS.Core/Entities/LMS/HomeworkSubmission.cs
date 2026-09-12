using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.LMS
{
    public class HomeworkSubmission : BaseTenantEntity
    {
        public long HomeworkId { get; set; }
        public long StudentId { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public string? FileUrl { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = "NotDone"; // Submitted/Late/NotDone
        public string? Feedback { get; set; }
        public decimal? Mark { get; set; }

        public virtual Homework? Homework { get; set; }
        public virtual Student? Student { get; set; }
    }
}
