using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students;

public class Student : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long PersonId { get; set; }
    public long? UserId { get; set; }
    public long? AdmissionApplicantId { get; set; }
    [Required, MaxLength(50)] public string StudentCode { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? Gender { get; set; }
    [MaxLength(20)] public string? BloodGroup { get; set; }
    [MaxLength(100)] public string? Religion { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public DateOnly AdmissionDate { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    [MaxLength(20)] public string PreferredLanguage { get; set; } = "bn-BD";
    [MaxLength(50)] public string StatusCode { get; set; } = "Active";
    public DateTime? PersonDataSnapshotAt { get; set; }
}

public class Guardian : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long PersonId { get; set; }
    public long? UserId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(150)] public string? Occupation { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public DateTime? PersonDataSnapshotAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StudentGuardian : BaseTenantEntity
{
    public long StudentId { get; set; }
    public long GuardianId { get; set; }
    [Required, MaxLength(50)] public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; } = true;
    public bool CanReceiveEmail { get; set; } = true;
    public bool IsAuthorizedPickup { get; set; }
    public bool IsFinancialContact { get; set; }
    public bool IsEmergencyContact { get; set; }
}

public class StudentPromotionRecord : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long FromEnrollmentId { get; set; }
    public long ToEnrollmentId { get; set; }
    public StudentProgressionDecisionType Decision { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public long ProcessedByUserId { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
}

public class StudentExitRecord : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public StudentExitType ExitType { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DueAtExit { get; set; }
    public bool FeesCleared { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public long ProcessedByUserId { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
    [MaxLength(1000)] public string? ConductRemark { get; set; }
}

public class TransferRequest : BaseEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long SourceTenantId { get; set; }
    public long SourceStudentId { get; set; }
    public long SourceEnrollmentId { get; set; }
    public long? DestinationTenantId { get; set; }
    public long? DestinationStudentId { get; set; }
    public TransferRequestState State { get; set; } = TransferRequestState.Draft;
    [MaxLength(1000)] public string? Reason { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public long RequestedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public long? AcceptedByUserId { get; set; }
    public DateTime? RejectedAt { get; set; }
    public long? RejectedByUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TransferCertificate : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long? TransferRequestId { get; set; }
    [Required, MaxLength(50)] public string CertificateNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
    [MaxLength(1000)] public string? ConductRemark { get; set; }
    public bool FeesCleared { get; set; }
    public long IssuedByUserId { get; set; }
    public long? FileAssetId { get; set; }
}

public class StudentStatusHistory : BaseTenantEntity
{
    public long StudentId { get; set; }
    [Required, MaxLength(50)] public string FromStatusCode { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string ToStatusCode { get; set; } = string.Empty;
    public DateTime EffectiveAt { get; set; } = DateTime.UtcNow;
    public long ChangedByUserId { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
}
