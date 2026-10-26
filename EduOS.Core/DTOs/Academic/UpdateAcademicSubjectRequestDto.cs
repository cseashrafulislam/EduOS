using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

/// <summary>Subject metadata only. The canonical subject code is immutable after creation.</summary>
public sealed class UpdateAcademicSubjectRequestDto
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? ShortName { get; set; }
    [Range(typeof(decimal), "0", "1000")] public decimal DefaultCreditHours { get; set; }
    public bool IsActive { get; set; } = true;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
