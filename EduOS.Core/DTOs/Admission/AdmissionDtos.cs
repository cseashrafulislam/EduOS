namespace EduOS.Core.DTOs.Admission;

public class AdmissionIntakeFormDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public long AcademicProgramId { get; set; }
    public string AcademicProgramName { get; set; } = string.Empty;
    public long AcademicLevelId { get; set; }
    public string AcademicLevelName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int VersionNo { get; set; }
    public AdmissionFormState State { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public decimal ApplicationFee { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public IReadOnlyList<AdmissionFormFieldDto> Fields { get; set; } = Array.Empty<AdmissionFormFieldDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAdmissionIntakeFormRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long AcademicProgramId { get; set; }
    public long AcademicLevelId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int VersionNo { get; set; } = 1;
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal ApplicationFee { get; set; }
    [Required, MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public IReadOnlyList<SaveAdmissionFormFieldRequestDto> Fields { get; set; } = Array.Empty<SaveAdmissionFormFieldRequestDto>();
    public string? RowVersion { get; set; }
}

public class ChangeAdmissionFormStateRequestDto
{
    public AdmissionFormState State { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class AdmissionFormFieldDto
{
    public long Id { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public string? OptionsJson { get; set; }
    public string? ValidationJson { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAdmissionFormFieldRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(100)] public string FieldKey { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; } = CustomFieldDataType.Text;
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    [MaxLength(4000)] public string? OptionsJson { get; set; }
    [MaxLength(1000)] public string? ValidationJson { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class AdmissionApplicantDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid AdmissionIntakeFormReference { get; set; }
    public long? PersonId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public AdmissionApplicantState State { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public long? ReviewedByUserId { get; set; }
    public IReadOnlyList<AdmissionApplicantFieldValueDto> FieldValues { get; set; } = Array.Empty<AdmissionApplicantFieldValueDto>();
    public IReadOnlyList<AdmissionApplicantGuardianDto> Guardians { get; set; } = Array.Empty<AdmissionApplicantGuardianDto>();
    public IReadOnlyList<AdmissionApplicantDocumentDto> Documents { get; set; } = Array.Empty<AdmissionApplicantDocumentDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAdmissionApplicantRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid AdmissionIntakeFormReference { get; set; }
    public long? PersonId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? Gender { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    public IReadOnlyList<SaveAdmissionApplicantFieldValueRequestDto> FieldValues { get; set; } = Array.Empty<SaveAdmissionApplicantFieldValueRequestDto>();
    public IReadOnlyList<SaveAdmissionApplicantGuardianRequestDto> Guardians { get; set; } = Array.Empty<SaveAdmissionApplicantGuardianRequestDto>();
    public string? RowVersion { get; set; }
}

public class SubmitAdmissionApplicantRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class AdmissionApplicantFieldValueDto
{
    public long AdmissionFormFieldId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class SaveAdmissionApplicantFieldValueRequestDto
{
    public long AdmissionFormFieldId { get; set; }
    [MaxLength(4000)] public string? Value { get; set; }
}

public class AdmissionApplicantGuardianDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string RelationCode { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Occupation { get; set; }
    public bool IsPrimary { get; set; }
}

public class SaveAdmissionApplicantGuardianRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string RelationCode { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(150)] public string? Occupation { get; set; }
    public bool IsPrimary { get; set; }
}

public class AdmissionApplicantDocumentDto
{
    public long Id { get; set; }
    public long FileAssetId { get; set; }
    public string DocumentTypeCode { get; set; } = string.Empty;
    public int VersionNo { get; set; }
    public bool IsVerified { get; set; }
    public long? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationNote { get; set; }
}

public class AddAdmissionApplicantDocumentRequestDto
{
    public long FileAssetId { get; set; }
    [Required, MaxLength(100)] public string DocumentTypeCode { get; set; } = string.Empty;
}

public class VerifyAdmissionApplicantDocumentRequestDto
{
    public bool Verified { get; set; }
    [MaxLength(1000)] public string? VerificationNote { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class AdmissionTestDto
{
    public long Id { get; set; }
    public Guid AdmissionIntakeFormReference { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly TestDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal PassMarks { get; set; }
    public string? Venue { get; set; }
    public bool IsPublished { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAdmissionTestRequestDto
{
    public Guid AdmissionIntakeFormReference { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public DateOnly TestDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    [Range(1, 1440)] public int DurationMinutes { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal TotalMarks { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal PassMarks { get; set; }
    [MaxLength(200)] public string? Venue { get; set; }
    public string? RowVersion { get; set; }
}

public class AdmissionResultDto
{
    public long Id { get; set; }
    public long AdmissionTestId { get; set; }
    public Guid AdmissionApplicantReference { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public decimal ObtainedMarks { get; set; }
    public bool IsPassed { get; set; }
    public int? MeritPosition { get; set; }
    public string? Grade { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAdmissionResultRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long AdmissionTestId { get; set; }
    public Guid AdmissionApplicantReference { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal ObtainedMarks { get; set; }
    [MaxLength(20)] public string? Grade { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    public string? RowVersion { get; set; }
}

public class AdmissionDecisionDto
{
    public long Id { get; set; }
    public Guid AdmissionApplicantReference { get; set; }
    public AdmissionDecisionState State { get; set; }
    public long? OfferedAcademicBatchId { get; set; }
    public string? OfferedAcademicBatchName { get; set; }
    public DateTime? OfferedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? Note { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class MakeAdmissionDecisionRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid AdmissionApplicantReference { get; set; }
    public AdmissionDecisionState State { get; set; }
    public long? OfferedAcademicBatchId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    [Required] public string ApplicantRowVersion { get; set; } = string.Empty;
}
