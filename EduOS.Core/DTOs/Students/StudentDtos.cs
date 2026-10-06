namespace EduOS.Core.DTOs.Students;

public class StudentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long PersonId { get; set; }
    public long? UserId { get; set; }
    public long? AdmissionApplicantId { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? BloodGroup { get; set; }
    public string? Religion { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateOnly AdmissionDate { get; set; }
    public string? PhotoUrl { get; set; }
    public string PreferredLanguage { get; set; } = "bn-BD";
    public string StatusCode { get; set; } = "Active";
    public bool IsActive { get; set; }
    public CurrentEnrollmentSummaryDto? CurrentEnrollment { get; set; }
    public IReadOnlyList<StudentGuardianDto> Guardians { get; set; } = Array.Empty<StudentGuardianDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class CurrentEnrollmentSummaryDto
{
    public Guid EnrollmentReference { get; set; }
    public string AcademicYear { get; set; } = string.Empty;
    public string? AcademicTerm { get; set; }
    public string Program { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
}

public class CreateStudentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long? PersonId { get; set; }
    public long? AdmissionApplicantId { get; set; }
    [Required, MaxLength(50)] public string StudentCode { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? Gender { get; set; }
    [MaxLength(20)] public string? BloodGroup { get; set; }
    [MaxLength(100)] public string? Religion { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public DateOnly AdmissionDate { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    [Required, MaxLength(20)] public string PreferredLanguage { get; set; } = "bn-BD";
}

public class UpdateStudentRequestDto
{
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? Gender { get; set; }
    [MaxLength(20)] public string? BloodGroup { get; set; }
    [MaxLength(100)] public string? Religion { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    [Required, MaxLength(20)] public string PreferredLanguage { get; set; } = "bn-BD";
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class ChangeStudentStatusRequestDto
{
    [Required, MaxLength(50)] public string StatusCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class GuardianDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long PersonId { get; set; }
    public long? UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Occupation { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveGuardianRequestDto
{
    public long? PersonId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(150)] public string? Occupation { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class StudentGuardianDto
{
    public long Id { get; set; }
    public Guid StudentReference { get; set; }
    public Guid GuardianReference { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; }
    public bool CanReceiveEmail { get; set; }
    public bool IsAuthorizedPickup { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class LinkStudentGuardianRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    public Guid GuardianReference { get; set; }
    [Required, MaxLength(50)] public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; } = true;
    public bool CanReceiveEmail { get; set; } = true;
    public bool IsAuthorizedPickup { get; set; }
}

public class UpdateStudentGuardianRequestDto
{
    [Required, MaxLength(50)] public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; } = true;
    public bool CanReceiveEmail { get; set; } = true;
    public bool IsAuthorizedPickup { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class StudentPromotionRecordDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public Guid FromEnrollmentReference { get; set; }
    public Guid ToEnrollmentReference { get; set; }
    public StudentProgressionDecisionType Decision { get; set; }
    public DateTime ProcessedAt { get; set; }
    public long ProcessedByUserId { get; set; }
    public string? Note { get; set; }
}

public class PromoteStudentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid SourceEnrollmentReference { get; set; }
    public long TargetCampusId { get; set; }
    public long TargetAcademicYearId { get; set; }
    public long? TargetAcademicTermId { get; set; }
    public long TargetAcademicProgramId { get; set; }
    public long TargetAcademicLevelId { get; set; }
    public long TargetAcademicBatchId { get; set; }
    public long TargetAcademicCurriculumId { get; set; }
    public long? TargetAcademicTrackId { get; set; }
    public long? TargetMediumId { get; set; }
    public long? TargetShiftId { get; set; }
    [Required, MaxLength(50)] public string TargetRollNo { get; set; } = string.Empty;
    public StudentProgressionDecisionType Decision { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    [Required] public string StudentRowVersion { get; set; } = string.Empty;
    [Required] public string SourceEnrollmentRowVersion { get; set; } = string.Empty;
}

public class StudentExitRecordDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public StudentExitType ExitType { get; set; }
    public decimal DueAtExit { get; set; }
    public bool FeesCleared { get; set; }
    public DateTime ProcessedAt { get; set; }
    public long ProcessedByUserId { get; set; }
    public string? Reason { get; set; }
    public string? ConductRemark { get; set; }
}

public class FinalizeStudentExitRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public StudentExitType ExitType { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
    [MaxLength(1000)] public string? ConductRemark { get; set; }
    [Required] public string StudentRowVersion { get; set; } = string.Empty;
    [Required] public string EnrollmentRowVersion { get; set; } = string.Empty;
}

public class TransferRequestDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long SourceTenantId { get; set; }
    public Guid SourceStudentReference { get; set; }
    public Guid SourceEnrollmentReference { get; set; }
    public long? DestinationTenantId { get; set; }
    public TransferRequestState State { get; set; }
    public string? Reason { get; set; }
    public DateTime RequestedAt { get; set; }
    public long RequestedByUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateTransferRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    public Guid EnrollmentReference { get; set; }
    public long? DestinationTenantId { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
}

public class ReviewTransferRequestDto
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class TransferCertificateDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid? TransferRequestReference { get; set; }
    public string CertificateNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public string? Reason { get; set; }
    public string? ConductRemark { get; set; }
    public bool FeesCleared { get; set; }
    public long IssuedByUserId { get; set; }
    public long? FileAssetId { get; set; }
}
