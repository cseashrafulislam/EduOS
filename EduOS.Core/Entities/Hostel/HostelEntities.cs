using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Hostel;

public class Hostel : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long CampusId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(50)] public string? GenderRestriction { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HostelRoom : BaseTenantEntity
{
    public long HostelId { get; set; }
    [Required, MaxLength(50)] public string RoomNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal RentPerBed { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HostelBed : BaseTenantEntity
{
    public long HostelRoomId { get; set; }
    [Required, MaxLength(50)] public string BedNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class StudentHostelAllocation : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long HostelBedId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MonthlyRent { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SecurityDeposit { get; set; }
    public long? FeeHeadId { get; set; }
    public HostelAllocationState State { get; set; } = HostelAllocationState.Active;
}
