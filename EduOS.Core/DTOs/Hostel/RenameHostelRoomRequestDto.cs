using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

/// <summary>Corrects only the canonical physical room label; no pricing or allocation mutation.</summary>
public sealed class RenameHostelRoomRequestDto
{
    [Required, StringLength(50, MinimumLength = 1)]
    public string RoomNumber { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
