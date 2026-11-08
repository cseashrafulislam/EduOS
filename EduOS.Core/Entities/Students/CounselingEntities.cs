using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students;

/// <summary>Restricted case metadata. Narrative is protected at rest by the service.</summary>
public sealed class CounselingCase : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long CampusId { get; set; }
    [Required, MaxLength(20)] public string State { get; set; } = "Requested";
    public bool IsUrgent { get; set; }
    public long OpenedByUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public long? AssignedCounselorUserId { get; set; }
    public long? AssignedByUserId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public long? ClosedByUserId { get; set; }
    [Required, MaxLength(8000)] public string ProtectedSummary { get; set; } = "";
    public bool GuardianContactConsented { get; set; }
    public DateTime? GuardianConsentAtUtc { get; set; }
}

/// <summary>Private immutable event/note, with encrypted narrative and a sealed access trail.</summary>
public sealed class CounselingCaseEvent : BaseTenantEntity
{
    public long CounselingCaseId { get; set; }
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(24)] public string Action { get; set; } = "";
    [Required, MaxLength(8000)] public string ProtectedNote { get; set; } = "";
    public DateTime? AppointmentAtUtc { get; set; }
    public long ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
