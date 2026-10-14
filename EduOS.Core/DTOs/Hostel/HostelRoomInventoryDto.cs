using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Hostel;
/// <summary>Tenant/campus-scoped physical inventory without student or finance fields.</summary>
public sealed class HostelRoomInventoryDto
{
    public long Id { get; set; }
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int ActiveBeds { get; set; }
    public int OccupiedBeds { get; set; }
    public bool IsActive { get; set; }
    public bool HostelActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class SetHostelRoomStateRequestDto
{
    public bool IsActive { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateHostelRoomRequestDto
{
    [Required] public Guid HostelReference { get; set; }
    [Required, StringLength(50, MinimumLength = 1)] public string RoomNumber { get; set; } = string.Empty;
    [Range(1, 1000)] public int Capacity { get; set; }
}
