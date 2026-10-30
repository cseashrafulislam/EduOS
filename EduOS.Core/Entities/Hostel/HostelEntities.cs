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
    // Optional immutable lineage: successor allocation references the previous stay.
    // Only the occupied-transfer command may populate this; never rewrite a source bed.
    public long? PreviousAllocationId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MonthlyRent { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SecurityDeposit { get; set; }
    public long? FeeHeadId { get; set; }
    public HostelAllocationState State { get; set; } = HostelAllocationState.Active;
}

public enum HostelApplicationState
{
    Submitted = 1,
    Waitlisted = 2,
    Offered = 3,
    Accepted = 4,
    Rejected = 5,
    Withdrawn = 6,
    Expired = 7
}

/// <summary>An application is a request, never a capacity hold or a financial transaction.</summary>
public sealed class HostelApplication : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public Guid AllocationRequestId { get; set; } = Guid.NewGuid();
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long CampusId { get; set; }
    public long HostelId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public HostelApplicationState State { get; set; } = HostelApplicationState.Submitted;
    public DateTime SubmittedAtUtc { get; set; }
    public long SubmittedByUserId { get; set; }
    [MaxLength(200)] public string StudentNameAtSubmission { get; set; } = string.Empty;
    [MaxLength(50)] public string StudentCodeAtSubmission { get; set; } = string.Empty;
    [MaxLength(150)] public string HostelNameAtSubmission { get; set; } = string.Empty;
    [MaxLength(500)] public string? Note { get; set; }
    public long? OfferedBedId { get; set; }
    public DateTime? OfferExpiresAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public long? DecidedByUserId { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
    public long? AllocationId { get; set; }
}

/// <summary>Immutable audit trail of every application state transition.</summary>
public sealed class HostelApplicationEvent : BaseTenantEntity
{
    public long HostelApplicationId { get; set; }
    public HostelApplicationState? FromState { get; set; }
    public HostelApplicationState ToState { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long ActorUserId { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

/// <summary>Verified, time-limited guardian authority; pickup rights are unrelated.</summary>
public sealed class HostelApplicationAuthority : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long StudentGuardianId { get; set; }
    public long StudentId { get; set; }
    public long GuardianId { get; set; }
    public long CampusId { get; set; }
    [Required, MaxLength(150)] public string EvidenceReference { get; set; } = string.Empty;
    public DateTime GrantedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public long GrantedByUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public long? RevokedByUserId { get; set; }
    [MaxLength(500)] public string? RevocationReason { get; set; }
}
