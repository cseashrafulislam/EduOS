namespace EduOS.Core.DTOs.Academic;

/// <summary>Tenant- and teacher-scoped subject offering choice for lesson-plan authoring.</summary>
public sealed class LessonOfferingOptionDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
