using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Admission;

public class AdmissionIntakeForm : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long AcademicProgramId { get; set; }
    public long AcademicLevelId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    public long? ShiftId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public int VersionNo { get; set; } = 1;
    public AdmissionFormState State { get; set; } = AdmissionFormState.Draft;
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ApplicationFee { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
}

public class AdmissionFormField : BaseTenantEntity
{
    public long AdmissionIntakeFormId { get; set; }
    [Required, MaxLength(100)] public string FieldKey { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; } = CustomFieldDataType.Text;
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    [MaxLength(4000)] public string? OptionsJson { get; set; }
    [MaxLength(1000)] public string? ValidationJson { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AdmissionApplicant : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long AdmissionIntakeFormId { get; set; }
    public long? PersonId { get; set; }
    [Required, MaxLength(50)] public string ApplicationNumber { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? Gender { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public AdmissionApplicantState State { get; set; } = AdmissionApplicantState.Draft;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public long? ReviewedByUserId { get; set; }
    public long? ConvertedStudentId { get; set; }
    public long? ConvertedEnrollmentId { get; set; }
    public DateTime? ConvertedAt { get; set; }
}

public class AdmissionApplicantFieldValue : BaseTenantEntity
{
    public long AdmissionApplicantId { get; set; }
    public long AdmissionFormFieldId { get; set; }
    [MaxLength(4000)] public string? Value { get; set; }
}

public class AdmissionApplicantGuardian : BaseTenantEntity
{
    public long AdmissionApplicantId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string RelationCode { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(150)] public string? Occupation { get; set; }
    public bool IsPrimary { get; set; }
}

public class AdmissionApplicantDocument : BaseTenantEntity
{
    public long AdmissionApplicantId { get; set; }
    public long FileAssetId { get; set; }
    [Required, MaxLength(100)] public string DocumentTypeCode { get; set; } = string.Empty;
    public int VersionNo { get; set; } = 1;
    public bool IsVerified { get; set; }
    public long? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    [MaxLength(1000)] public string? VerificationNote { get; set; }
}

public class AdmissionTest : BaseTenantEntity
{
    public long AdmissionIntakeFormId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public DateOnly TestDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public int DurationMinutes { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalMarks { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PassMarks { get; set; }
    [MaxLength(200)] public string? Venue { get; set; }
    public bool IsPublished { get; set; }
}

public class AdmissionResult : BaseTenantEntity
{
    public long AdmissionTestId { get; set; }
    public long AdmissionApplicantId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ObtainedMarks { get; set; }
    public bool IsPassed { get; set; }
    public int? MeritPosition { get; set; }
    [MaxLength(20)] public string? Grade { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
}

public class AdmissionDecision : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long AdmissionApplicantId { get; set; }
    public AdmissionDecisionState State { get; set; } = AdmissionDecisionState.Pending;
    public long? OfferedAcademicBatchId { get; set; }
    public DateTime? OfferedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
}

public class AdmissionPayment : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long AdmissionApplicantId { get; set; }
    [Required, MaxLength(50)] public string ReceiptNumber { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public PaymentState State { get; set; } = PaymentState.Initiated;
    [MaxLength(100)] public string? ProviderCode { get; set; }
    [MaxLength(150)] public string? ProviderTransactionId { get; set; }
    [MaxLength(150)] public string? ExternalReference { get; set; }
    public long? ReceivedByUserId { get; set; }
    public long? JournalId { get; set; }
    public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    [MaxLength(1000)] public string? FailureReason { get; set; }
}
