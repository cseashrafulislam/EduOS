using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

/// <summary>Create one physical bed in an existing room. No student or finance data.</summary>
public sealed class CreateHostelBedRequestDto
{
    [Range(1, long.MaxValue)] public long HostelRoomId { get; set; }
    [Required, StringLength(50, MinimumLength = 1)] public string BedNumber { get; set; } = string.Empty;
    [Required] public string RoomRowVersion { get; set; } = string.Empty;
}
