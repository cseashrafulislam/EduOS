using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class TransferCertificate : BaseTenantEntity
    {
        public Guid PublicId { get; set; } = Guid.NewGuid();
        public Guid ClientRequestId { get; set; }
        public long StudentId { get; set; }
        public string TcNo { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public string? Reason { get; set; }
        public int LastClassId { get; set; }
        public int LastSectionId { get; set; }
        public int LastAcademicYearId { get; set; }
        public string LastRoll { get; set; } = string.Empty;
        public string? ConductRemark { get; set; }
        public bool FeesCleared { get; set; }
        public long IssuedBy { get; set; }
        public virtual Student? Student { get; set; }
        public virtual Class? LastClass { get; set; }
    }
}
