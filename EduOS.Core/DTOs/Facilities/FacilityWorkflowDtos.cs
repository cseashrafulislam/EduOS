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

public sealed class FacilityTransitionDto
{
    public Guid ClientCommandId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Note { get; set; }
    public DateTime? SlaDueAtUtc { get; set; }
    public long? TechnicianUserId { get; set; }
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
    public DateTime? ScheduledStartUtc { get; set; }
    public DateTime? ScheduledEndUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public string? CompletionEvidence { get; set; }
    public bool? SlaBreached { get; set; }
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