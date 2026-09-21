using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

public sealed class HostelRoomDto
{
    public long Id { get; set; }
    public long HostelId { get; set; }
    public string HostelName { get; set; } = string.Empty;
    public string RoomNo { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int OccupiedBeds { get; set; }
    public int AvailableBeds { get; set; }
    public decimal RentPerBed { get; set; }
}

public sealed class StudentHostelDto
{
    public long Id { get; set; }
    public long StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public long HostelId { get; set; }
    public string HostelName { get; set; } = string.Empty;
    public long HostelRoomId { get; set; }
    public string RoomNo { get; set; } = string.Empty;
    public string? BedNo { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal MonthlyRent { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AllocateHostelDto
{
    [Range(1, long.MaxValue)] public long StudentId { get; set; }
    [Range(1, long.MaxValue)] public long HostelRoomId { get; set; }
    [StringLength(50)] public string? BedNo { get; set; }
    public DateTime StartDate { get; set; }
    [Range(0, 100000000)] public decimal? MonthlyRent { get; set; }
}

public sealed class CloseHostelAllocationDto
{
    public DateTime? EndDate { get; set; }
}
