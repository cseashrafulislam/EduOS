namespace EduOS.Core.DTOs.Academic;

public sealed class StudentOptionalSubjectWorkspaceDto
{
    public Guid StudentReference { get; set; }
    public Guid EnrollmentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public IReadOnlyList<OptionalSubjectOfferingDto> Offerings { get; set; } = Array.Empty<OptionalSubjectOfferingDto>();
    public IReadOnlyList<StudentSubjectRegistrationDto> Registrations { get; set; } = Array.Empty<StudentSubjectRegistrationDto>();
}

public sealed class OptionalSubjectOfferingDto
{
    public Guid Reference { get; set; }
    public string Code { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public int Capacity { get; set; }
}
