namespace EduOS.Core.DTOs.Hostel;

public class HostelDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? GenderRestriction { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveHostelRequestDto
{
    public long CampusId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(50)] public string? GenderRestriction { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class HostelRoomDto
{
    public long Id { get; set; }
    public Guid HostelReference { get; set; }
    public string HostelName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public decimal RentPerBed { get; set; }
    public int ActiveAllocations { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveHostelRoomRequestDto
{
    public Guid HostelReference { get; set; }
    [Required, MaxLength(50)] public string RoomNumber { get; set; } = string.Empty;
    [Range(1, 1000)] public int Capacity { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal RentPerBed { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class HostelBedDto
{
    public long Id { get; set; }
    public long HostelRoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsOccupied { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveHostelBedRequestDto
{
    public long HostelRoomId { get; set; }
    [Required, MaxLength(50)] public string BedNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class StudentHostelAllocationDto
{
    public long Id { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public Guid HostelReference { get; set; }
    public string HostelName { get; set; } = string.Empty;
    public long HostelRoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public long HostelBedId { get; set; }
    public string BedNumber { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal MonthlyRent { get; set; }
    public HostelAllocationState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AllocateStudentHostelRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public long HostelBedId { get; set; }
    public DateOnly StartDate { get; set; }
    public decimal? MonthlyRent { get; set; }
}

public class CloseStudentHostelAllocationRequestDto
{
    public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

// Backward-compatible API contract aliases; domain entities remain canonical.
public class StudentHostelDto : StudentHostelAllocationDto { }
public class AllocateHostelDto : AllocateStudentHostelRequestDto { }
public class CloseHostelAllocationDto : CloseStudentHostelAllocationRequestDto { }
