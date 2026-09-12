using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students;

public class StudentExitRecord : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long? EnrollmentId { get; set; }
    public string ExitType { get; set; } = string.Empty; // Transfer/Completed/Dropout
    public string? CertificateNo { get; set; }
    public int AcademicYearId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public int? GroupId { get; set; }
    public string Roll { get; set; } = string.Empty;
    public decimal DueAtExit { get; set; }
    public bool FeesCleared { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public long ProcessedByUserId { get; set; }
    public string? Reason { get; set; }
    public string? ConductRemark { get; set; }
    public virtual Student? Student { get; set; }
    public virtual Enrollment? Enrollment { get; set; }
}
