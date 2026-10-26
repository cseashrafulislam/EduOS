using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

/// <summary>Post-creation level metadata; programme and code remain immutable.</summary>
public sealed class UpdateAcademicLevelRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Range(1, 1000)] public int LevelNo { get; set; }
    public bool IsPromotable { get; set; }
    public bool IsTerminalLevel { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(0, int.MaxValue)] public int DisplayOrder { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
