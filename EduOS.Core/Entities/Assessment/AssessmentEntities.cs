using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Assessment;

public class Assessment : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long? GradeSchemeId { get; set; }
    public int VersionNo { get; set; } = 1;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public AssessmentKind Type { get; set; } = AssessmentKind.Exam;
    public AssessmentState State { get; set; } = AssessmentState.Draft;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
}

public class AssessmentSubject : BaseTenantEntity
{
    public long AssessmentId { get; set; }
    public long SubjectOfferingId { get; set; }
    public long? GradeSchemeId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal FullMarks { get; set; } = 100;
    [Column(TypeName = "decimal(18,2)")] public decimal PassMarks { get; set; } = 33;
    [Column(TypeName = "decimal(18,2)")] public decimal Weightage { get; set; } = 100;
}

public class AssessmentSchedule : BaseTenantEntity
{
    public long AssessmentSubjectId { get; set; }
    public DateOnly AssessmentDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public long? RoomId { get; set; }
    [MaxLength(1000)] public string? Instructions { get; set; }
}

public class StudentAssessmentMark : BaseTenantEntity
{
    public long AssessmentSubjectId { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    [MaxLength(20)] public string? GradeLetter { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? GradePoint { get; set; }
    [MaxLength(1000)] public string? Remarks { get; set; }
    public long EnteredByUserId { get; set; }
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
}

public class GradeScheme : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public long? AcademicProgramId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class GradeRule : BaseTenantEntity
{
    public long GradeSchemeId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MinMarks { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MaxMarks { get; set; }
    [Required, MaxLength(20)] public string GradeLetter { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal GradePoint { get; set; }
    public bool IsFailGrade { get; set; }
    public int DisplayOrder { get; set; }
}

public class ResultPublication : BaseTenantEntity
{
    public long AssessmentId { get; set; }
    public long AcademicBatchId { get; set; }
    public int VersionNo { get; set; } = 1;
    public ResultPublicationState State { get; set; } = ResultPublicationState.Draft;
    public DateTime? PublishedAt { get; set; }
    public long? PublishedByUserId { get; set; }
    public bool VisibleToStudent { get; set; } = true;
    public bool VisibleToGuardian { get; set; } = true;
    [MaxLength(1000)] public string? PublishNote { get; set; }
}

public class CertificateTemplate : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required] public string HtmlTemplate { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CertificateIssue : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CertificateTemplateId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    [Required, MaxLength(50)] public string CertificateNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public long IssuedByUserId { get; set; }
    public long? FileAssetId { get; set; }
    [MaxLength(128)] public string? VerificationCodeHash { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class AssessmentComponent : BaseTenantEntity
{
    public long AssessmentSubjectId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal FullMarks { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PassMarks { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Weightage { get; set; } = 100;
    public bool IsMandatoryPass { get; set; }
    public int DisplayOrder { get; set; }
}

public class StudentAssessmentComponentMark : BaseTenantEntity
{
    public long AssessmentComponentId { get; set; }
    public long StudentAssessmentMarkId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    public long EnteredByUserId { get; set; }
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
}

public class StudentResultSummary : BaseTenantEntity
{
    public long ResultPublicationId { get; set; }
    public long AssessmentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public int PublicationVersionNo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalMarks { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ObtainedMarks { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? Percentage { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? GPA { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? CGPA { get; set; }
    [MaxLength(20)] public string? GradeLetter { get; set; }
    public int? MeritPosition { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}

public class TranscriptIssue : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long StudentId { get; set; }
    [Required, MaxLength(50)] public string TranscriptNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public long IssuedByUserId { get; set; }
    public long? FileAssetId { get; set; }
    [MaxLength(128)] public string? VerificationCodeHash { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}
