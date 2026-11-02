using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Facilities;

/// <summary>Independent campus maintenance request; never a stock or finance ledger.</summary>
public sealed class FacilityServiceRequest : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long FacilitySpaceId { get; set; }
    public long RequesterUserId { get; set; }
    [Required, MaxLength(40)] public string CategoryCode { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PriorityCode { get; set; } = "Normal";
    [Required, MaxLength(200)] public string Summary { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    [Required, MaxLength(30)] public string StateCode { get; set; } = "Submitted";
    public long? TriagedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? SlaDueAtUtc { get; set; }
    [MaxLength(500)] public string? DecisionReason { get; set; }
}

public sealed class FacilityWorkOrder : BaseTenantEntity
{
    public long FacilityServiceRequestId { get; set; }
    [Required, MaxLength(30)] public string StateCode { get; set; } = "Approved";
    public long? AssignedTechnicianUserId { get; set; }
    // Current vendor decision is a work-order projection. Every decision is also
    // recorded in FacilityWorkflowEvents; no stock/GL posting is implied.
    public long? VendorSupplierId { get; set; }
    [MaxLength(20)] public string? VendorStateCode { get; set; }
    public long? VendorProposedByUserId { get; set; }
    public long? VendorApprovedByUserId { get; set; }
    public DateTime? VendorStartUtc { get; set; }
    public DateTime? VendorEndUtc { get; set; }
    public DateTime? ScheduledStartUtc { get; set; }
    public DateTime? ScheduledEndUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public long? VerifiedByUserId { get; set; }
    [MaxLength(1000)] public string? CompletionEvidence { get; set; }
    [MaxLength(500)] public string? PauseReason { get; set; }
}

/// <summary>Append-only transition evidence; command ID prevents duplicate side effects.</summary>
public sealed class FacilityWorkflowEvent : BaseTenantEntity
{
    public long FacilityServiceRequestId { get; set; }
    public Guid ClientCommandId { get; set; }
    public long ActorUserId { get; set; }
    [Required, MaxLength(30)] public string ActionCode { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string FromState { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string ToState { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Note { get; set; }
}