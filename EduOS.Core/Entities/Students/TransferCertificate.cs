using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class TransferCertificate : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public string TcNo { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public string? Reason { get; set; }
        public int LastClassId { get; set; }
        public string? ConductRemark { get; set; }
        public bool FeesCleared { get; set; } = false;
        public int IssuedBy { get; set; }

        public virtual Student? Student { get; set; }
        public virtual Class? LastClass { get; set; }
    }
}
