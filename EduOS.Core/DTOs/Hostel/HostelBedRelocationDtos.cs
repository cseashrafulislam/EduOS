using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

/// <summary>Physical inventory relocation only; beds with any allocation history cannot move.</summary>
public sealed class RelocateUnusedHostelBedRequestDto
{
    [Range(1, long.MaxValue)] public long TargetRoomId { get; set; }
    [Required] public string BedRowVersion { get; set; } = string.Empty;
    [Required] public string TargetRoomRowVersion { get; set; } = string.Empty;
}

public sealed class HostelBedRelocationRoomDto
{
    public long Id { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int PhysicalBeds { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
