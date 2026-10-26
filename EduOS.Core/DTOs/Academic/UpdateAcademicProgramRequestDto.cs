using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

/// <summary>Programme metadata only. Campus allocation and immutable programme code are managed separately.</summary>
public sealed class UpdateAcademicProgramRequestDto
{
    public long? AcademicDepartmentId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? ShortName { get; set; }
    [Range(0, 1200)] public int DurationInMonths { get; set; }
    [MaxLength(150)] public string? AwardTitle { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsAdmissionOpen { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(0, int.MaxValue)] public int DisplayOrder { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
