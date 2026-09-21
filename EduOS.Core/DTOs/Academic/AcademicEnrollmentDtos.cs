using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Academics;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

public sealed class AcademicStudentEnrollmentDto
{
    public long Id { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long AcademicProgramId { get; set; }
    public long AcademicLevelId { get; set; }
    public long AcademicBatchId { get; set; }
    public string BatchName { get; set; } = string.Empty;
    public long AcademicCurriculumId { get; set; }
    public string CurriculumName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public DateTime EnrollmentDate { get; set; }
    public EnrollmentStatus EnrollmentStatus { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<StudentSubjectRegistrationDto> Subjects { get; set; } = [];
}

public sealed class CreateAcademicStudentEnrollmentDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    [Required, StringLength(50)] public string RollNo { get; set; } = string.Empty;
    public DateTime? EnrollmentDate { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
}

public sealed class StudentSubjectRegistrationDto
{
    public long Id { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long SubjectId { get; set; }
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public decimal CreditHours { get; set; }
    public bool IsRequired { get; set; }
    public SubjectRegistrationStatus Status { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class RequestOptionalSubjectDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long SubjectId { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
}

public sealed class DecideSubjectRegistrationDto
{
    public SubjectRegistrationStatus Status { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [StringLength(500)] public string? Remarks { get; set; }
}
