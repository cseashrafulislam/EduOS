using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduOS.Core.Entities.Academic;

public class StudentSubjectRegistration : BaseTenantEntity
{
    public Guid? ClientRequestId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long StudentId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long AcademicBatchId { get; set; }
    public long AcademicCurriculumId { get; set; }
    public long CurriculumSubjectId { get; set; }
    public long SubjectId { get; set; }

    public bool IsRequired { get; set; }
    public SubjectRegistrationStatus Status { get; set; } = SubjectRegistrationStatus.Pending;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public long? DecidedByUserId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal FullMarksSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PassMarksSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CreditHoursSnapshot { get; set; }

    [Required, MaxLength(50)]
    public string SubjectCodeSnapshot { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string SubjectNameSnapshot { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Remarks { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual StudentEnrollment? StudentEnrollment { get; set; }
    public virtual Student? Student { get; set; }
    public virtual AcademicYear? AcademicYear { get; set; }
    public virtual AcademicTerm? AcademicTerm { get; set; }
    public virtual AcademicBatch? AcademicBatch { get; set; }
    public virtual AcademicCurriculum? AcademicCurriculum { get; set; }
    public virtual CurriculumSubject? CurriculumSubject { get; set; }
    public virtual Subject? Subject { get; set; }
}

public enum SubjectRegistrationStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4
}
