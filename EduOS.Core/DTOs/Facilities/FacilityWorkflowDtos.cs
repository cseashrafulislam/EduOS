using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

public sealed class CreateFacilityRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Range(1, long.MaxValue)] public long FacilitySpaceId { get; set; }
    [Required, MaxLength(40)] public string CategoryCode { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PriorityCode { get; set; } = "Normal";
    [Required, MaxLength(200)] public string Summary { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
}

/// <summary>Manager-only, non-authoritative preview; the approved triage stores the actual UTC SLA.</summary>
public sealed class FacilitySlaRecommendationDto
{
    public long FacilityRequestId { get; set; }
    public long CampusId { get; set; }
    public string PriorityCode { get; set; } = string.Empty;
    public int? ConfiguredHours { get; set; }
    public DateTime? RecommendedDueUtc { get; set; }
}

public sealed class FacilityTransitionDto
{
    public Guid ClientCommandId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Note { get; set; }
    public DateTime? SlaDueAtUtc { get; set; }
    public long? TechnicianUserId { get; set; }
    public long? VendorSupplierId { get; set; }
    public DateTime? VendorStartUtc { get; set; }
    public DateTime? VendorEndUtc { get; set; }
    public DateTime? ScheduledStartUtc { get; set; }
    public DateTime? ScheduledEndUtc { get; set; }
}

public sealed class FacilityRequestDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public long FacilitySpaceId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string PriorityCode { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public long RequesterUserId { get; set; }
    public long? TriagedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? SlaDueAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public long? WorkOrderId { get; set; }
    public string? WorkOrderState { get; set; }
    public string? WorkOrderRowVersion { get; set; }
    public long? TechnicianUserId { get; set; }
    public long? VendorSupplierId { get; set; }
    public string? VendorStateCode { get; set; }
    public long? VendorProposedByUserId { get; set; }
    public long? VendorApprovedByUserId { get; set; }
    public DateTime? VendorStartUtc { get; set; }
    public DateTime? VendorEndUtc { get; set; }
    public DateTime? ScheduledStartUtc { get; set; }
    public DateTime? ScheduledEndUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public string? CompletionEvidence { get; set; }
    public bool? SlaBreached { get; set; }
}

/// <summary>Authorized active campus-space choice; IDs are canonical server values.</summary>
public sealed class FacilityLocationOptionDto
{
    public long CampusId { get; set; }
    public string CampusCode { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public long FacilitySpaceId { get; set; }
    public string SpaceCode { get; set; } = string.Empty;
    public string SpaceName { get; set; } = string.Empty;
}

/// <summary>Eligible campus technician, without contact or identity details.</summary>
public sealed class FacilityTechnicianOptionDto
{
    public long UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Approved procurement vendor, for read-only campus eligibility preview.</summary>
public sealed class FacilityVendorOptionDto
{
    public long SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
}

public sealed class FacilityEventDto
{
    public long Id { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public long ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Note { get; set; }
}

/// <summary>Aggregates only the caller's authorized campus and request scope.</summary>
public sealed class FacilityDashboardDto
{
    public int TotalRequests { get; set; }
    public int Submitted { get; set; }
    public int AwaitingApproval { get; set; }
    public int Rejected { get; set; }
    public int ApprovedUnscheduled { get; set; }
    public int CancellationPending { get; set; }
    public int Cancelled { get; set; }
    public int Scheduled { get; set; }
    public int InProgress { get; set; }
    public int PendingVerification { get; set; }
    public int Closed { get; set; }
    public int OverdueOpen { get; set; }
    public int ClosedLate { get; set; }
}
