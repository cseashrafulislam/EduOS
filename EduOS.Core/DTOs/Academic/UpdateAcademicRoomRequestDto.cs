using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

/// <summary>Only safe room metadata may change; campus, code and active state are immutable.</summary>
public sealed class UpdateAcademicRoomRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Range(0, 100000)] public int Capacity { get; set; }
    [MaxLength(100)] public string? RoomType { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
