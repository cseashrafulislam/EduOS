namespace EduOS.Core.DTOs.Academic;

/// <summary>Bounded teacher lookup result for assignment selectors.</summary>
public sealed class AcademicInstructorChoiceDto
{
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
