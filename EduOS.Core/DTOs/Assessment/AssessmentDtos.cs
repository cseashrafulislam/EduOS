namespace EduOS.Core.DTOs.Assessment;

public class AssessmentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public long? AcademicTermId { get; set; }
    public string? AcademicTermName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public AssessmentKind Type { get; set; }
    public AssessmentState State { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Remarks { get; set; }
    public IReadOnlyList<AssessmentSubjectDto> Subjects { get; set; } = Array.Empty<AssessmentSubjectDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAssessmentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public AssessmentKind Type { get; set; } = AssessmentKind.Exam;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    public string? RowVersion { get; set; }
}

public class ChangeAssessmentStateRequestDto
{
    public AssessmentState State { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class AssessmentSubjectDto
{
    public long Id { get; set; }
    public Guid SubjectOfferingReference { get; set; }
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public decimal Weightage { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAssessmentSubjectRequestDto
{
    public Guid AssessmentReference { get; set; }
    public Guid SubjectOfferingReference { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal FullMarks { get; set; } = 100;
    [Range(typeof(decimal), "0", "100000")] public decimal PassMarks { get; set; } = 33;
    [Range(typeof(decimal), "0", "100")] public decimal Weightage { get; set; } = 100;
    public string? RowVersion { get; set; }
}

public class AssessmentScheduleDto
{
    public long Id { get; set; }
    public long AssessmentSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public DateOnly AssessmentDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public long? RoomId { get; set; }
    public string? RoomName { get; set; }
    public string? Instructions { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAssessmentScheduleRequestDto
{
    public long AssessmentSubjectId { get; set; }
    public DateOnly AssessmentDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public long? RoomId { get; set; }
    [MaxLength(1000)] public string? Instructions { get; set; }
    public string? RowVersion { get; set; }
}

public class StudentAssessmentMarkDto
{
    public long Id { get; set; }
    public long AssessmentSubjectId { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    public string? GradeLetter { get; set; }
    public decimal? GradePoint { get; set; }
    public string? Remarks { get; set; }
    public long EnteredByUserId { get; set; }
    public DateTime EnteredAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveStudentAssessmentMarkRequestDto
{
    public long StudentSubjectRegistrationId { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    public string? RowVersion { get; set; }
}

public class SaveMarksRegisterRequestDto
{
    public long AssessmentSubjectId { get; set; }
    public IReadOnlyList<SaveStudentAssessmentMarkRequestDto> Marks { get; set; } = Array.Empty<SaveStudentAssessmentMarkRequestDto>();
}

public class GradeSchemeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public long? AcademicProgramId { get; set; }
    public string? AcademicProgramName { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<GradeRuleDto> Rules { get; set; } = Array.Empty<GradeRuleDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveGradeSchemeRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public long? AcademicProgramId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<SaveGradeRuleRequestDto> Rules { get; set; } = Array.Empty<SaveGradeRuleRequestDto>();
    public string? RowVersion { get; set; }
}

public class GradeRuleDto
{
    public long Id { get; set; }
    public decimal MinMarks { get; set; }
    public decimal MaxMarks { get; set; }
    public string GradeLetter { get; set; } = string.Empty;
    public decimal GradePoint { get; set; }
    public bool IsFailGrade { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveGradeRuleRequestDto
{
    public long? Id { get; set; }
    public decimal MinMarks { get; set; }
    public decimal MaxMarks { get; set; }
    [Required, MaxLength(20)] public string GradeLetter { get; set; } = string.Empty;
    public decimal GradePoint { get; set; }
    public bool IsFailGrade { get; set; }
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class ResultPublicationDto
{
    public long Id { get; set; }
    public Guid AssessmentReference { get; set; }
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public ResultPublicationState State { get; set; }
    public DateTime? PublishedAt { get; set; }
    public long? PublishedByUserId { get; set; }
    public bool VisibleToStudent { get; set; }
    public bool VisibleToGuardian { get; set; }
    public string? PublishNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class PublishResultRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid AssessmentReference { get; set; }
    public long AcademicBatchId { get; set; }
    public bool VisibleToStudent { get; set; } = true;
    public bool VisibleToGuardian { get; set; } = true;
    [MaxLength(1000)] public string? PublishNote { get; set; }
}

public class CertificateTemplateDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string HtmlTemplate { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveCertificateTemplateRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required] public string HtmlTemplate { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class CertificateIssueDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long CertificateTemplateId { get; set; }
    public Guid StudentReference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public string CertificateNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public long IssuedByUserId { get; set; }
    public long? FileAssetId { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class IssueCertificateRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long CertificateTemplateId { get; set; }
    public Guid StudentReference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public DateOnly IssueDate { get; set; }
}

public class RevokeCertificateRequestDto
{
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
