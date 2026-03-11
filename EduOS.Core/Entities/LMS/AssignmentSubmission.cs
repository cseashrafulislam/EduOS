using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.LMS
{
    public class AssignmentSubmission : TenantEntity
    {
        public int AssignmentId { get; set; }
        public int StudentId { get; set; }
        public string FileUrl { get; set; }
        public DateTime SubmitDate { get; set; }
        public decimal? Score { get; set; }
    }
}
