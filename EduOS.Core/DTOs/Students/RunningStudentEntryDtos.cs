using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Students;

/// <summary>Existing/running learner onboarding; not an admission decision or a second enrollment authority.</summary>
public sealed class CreateRunningStudentRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, StringLength(200, MinimumLength = 2)] public string FullName { get; set; } = string.Empty;
    [StringLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [StringLength(30)] public string? Gender { get; set; }
    [StringLength(30)] public string? Phone { get; set; }
    [StringLength(1000)] public string? Address { get; set; }
    public DateOnly AdmissionDate { get; set; }
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicCurriculumId { get; set; }
    [Required, StringLength(50)] public string RollNo { get; set; } = string.Empty;
    public DateOnly EnrollmentDate { get; set; }
    public RunningStudentGuardianDto? Guardian { get; set; }
}

public sealed class RunningStudentGuardianDto
{
    [Required, StringLength(200, MinimumLength = 2)] public string FullName { get; set; } = string.Empty;
    [Required, StringLength(50)] public string RelationCode { get; set; } = string.Empty;
    [StringLength(30)] public string? Phone { get; set; }
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    public bool CanReceiveSms { get; set; } = true;
    public bool CanReceiveEmail { get; set; } = true;
}

public sealed class RunningStudentCreatedDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Guid EnrollmentReference { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public bool GuardianLinked { get; set; }
}
