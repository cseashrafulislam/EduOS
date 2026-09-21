using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Academics;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic;

public sealed class StudentEnrollment : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long AcademicProgramId { get; set; }
    public long AcademicLevelId { get; set; }
    public long AcademicBatchId { get; set; }
    public long AcademicCurriculumId { get; set; }
    public long? MediumId { get; set; }
    public long? ShiftId { get; set; }
    public long? AcademicTrackId { get; set; }

    [Required, MaxLength(50)]
    public string RollNo { get; set; } = string.Empty;

    public DateTime EnrollmentDate { get; set; }
    public EnrollmentStatus EnrollmentStatus { get; set; } = EnrollmentStatus.Active;
    public bool IsCurrent { get; set; } = true;
    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Remarks { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual Student? Student { get; set; }
    public virtual Campus? Campus { get; set; }
    public virtual AcademicYear? AcademicYear { get; set; }
    public virtual AcademicTerm? AcademicTerm { get; set; }
    public virtual AcademicProgram? AcademicProgram { get; set; }
    public virtual AcademicLevel? AcademicLevel { get; set; }
    public virtual AcademicBatch? AcademicBatch { get; set; }
    public virtual AcademicCurriculum? AcademicCurriculum { get; set; }
    public virtual Medium? Medium { get; set; }
    public virtual Shift? Shift { get; set; }
    public virtual AcademicTrack? AcademicTrack { get; set; }
    public virtual ICollection<StudentSubjectRegistration> SubjectRegistrations { get; set; } = new List<StudentSubjectRegistration>();
}
