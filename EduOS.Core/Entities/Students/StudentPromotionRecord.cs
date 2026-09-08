using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Students;

/// <summary>
/// Immutable history for one completed academic progression. Current student
/// placement is updated separately; this record preserves the before/after fact.
/// </summary>
public class StudentPromotionRecord : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long FromEnrollmentId { get; set; }
    public long ToEnrollmentId { get; set; }
    public long FromAcademicYearId { get; set; }
    public long ToAcademicYearId { get; set; }
    public long FromClassId { get; set; }
    public long ToClassId { get; set; }
    public long FromSectionId { get; set; }
    public long ToSectionId { get; set; }
    public long? FromGroupId { get; set; }
    public long? ToGroupId { get; set; }
    public string FromRoll { get; set; } = string.Empty;
    public string ToRoll { get; set; } = string.Empty;
    public StudentProgressionDecision Decision { get; set; }
    public DateTime ProcessedAt { get; set; }
    public long ProcessedByUserId { get; set; }
    public string? Note { get; set; }

    public virtual Student? Student { get; set; }
    public virtual Enrollment? FromEnrollment { get; set; }
    public virtual Enrollment? ToEnrollment { get; set; }
    public virtual AcademicYear? FromAcademicYear { get; set; }
    public virtual AcademicYear? ToAcademicYear { get; set; }
    public virtual Class? FromClass { get; set; }
    public virtual Class? ToClass { get; set; }
    public virtual Section? FromSection { get; set; }
    public virtual Section? ToSection { get; set; }
    public virtual Group? FromGroup { get; set; }
    public virtual Group? ToGroup { get; set; }
}
