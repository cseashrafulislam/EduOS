namespace EduOS.Core.DTOs.Hostel;

/// <summary>Aggregate room capacity only; excludes student and financial details.</summary>
public sealed class HostelCapacityRowDto
{
    public long RoomId { get; set; }
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int ActiveBeds { get; set; }
    public int InactiveBeds { get; set; }
    public int OccupiedBeds { get; set; }
    public int AvailableBeds { get; set; }
    public bool HostelActive { get; set; }
    public bool RoomActive { get; set; }
    public bool HasRisk { get; set; }
}
