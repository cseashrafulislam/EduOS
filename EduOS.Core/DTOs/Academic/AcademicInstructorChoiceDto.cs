namespace EduOS.Core.DTOs.Academic;

/// <summary>Bounded teacher lookup result for assignment selectors.</summary>
public sealed class AcademicInstructorChoiceDto
{
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>One authorized SQL page of active teachers for a selected academic batch.</summary>
public sealed class AcademicInstructorChoicePageDto
{
    public IReadOnlyList<AcademicInstructorChoiceDto> Items { get; set; } = Array.Empty<AcademicInstructorChoiceDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
