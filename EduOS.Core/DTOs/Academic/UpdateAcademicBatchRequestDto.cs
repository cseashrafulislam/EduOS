using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

/// <summary>Safe post-creation section metadata. Enrollment identity and dates remain immutable.</summary>
public sealed class UpdateAcademicBatchRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Range(0, 100000)] public int Capacity { get; set; }
    [Range(0, int.MaxValue)] public int DisplayOrder { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
