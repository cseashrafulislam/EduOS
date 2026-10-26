using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

/// <summary>Track metadata only. Programme scope and code are immutable after creation.</summary>
public sealed class UpdateAcademicTrackRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(0, int.MaxValue)] public int DisplayOrder { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
