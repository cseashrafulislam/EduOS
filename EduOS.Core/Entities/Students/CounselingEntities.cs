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
    public Guid? RedactionRequestId { get; set; }
    public long? RedactionRequestedByUserId { get; set; }
    public DateTime? RedactionRequestedAtUtc { get; set; }
    public DateTime? RedactedAtUtc { get; set; }
    public long? RedactedByUserId { get; set; }
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

/// <summary>One authoritative, independently approved safeguarding policy per campus.</summary>
public sealed class CounselingCampusPolicy : BaseTenantEntity
{
    public long CampusId { get; set; }
    [Range(30, 3650)] public int RetentionDays { get; set; }
    public bool LegalHold { get; set; }
    [Required, MaxLength(350)] public string EmergencyGuidanceEn { get; set; } = "";
    [Required, MaxLength(350)] public string EmergencyGuidanceBn { get; set; } = "";
    public long ApprovedByUserId { get; set; }
    public DateTime ApprovedAtUtc { get; set; }
}

/// <summary>Immutable maker/checker proposal; effective values live only in CounselingCampusPolicy.</summary>
public sealed class CounselingPolicyChange : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public Guid? ReviewRequestId { get; set; }
    public long CampusId { get; set; }
    [Required, MaxLength(12)] public string State { get; set; } = "Submitted";
    [Range(30, 3650)] public int RetentionDays { get; set; }
    public bool LegalHold { get; set; }
    [Required, MaxLength(350)] public string EmergencyGuidanceEn { get; set; } = "";
    [Required, MaxLength(350)] public string EmergencyGuidanceBn { get; set; } = "";
    [MaxLength(8)] public byte[]? ExpectedPolicyVersion { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = "";
    public long ProposedByUserId { get; set; }
    public DateTime ProposedAtUtc { get; set; } = DateTime.UtcNow;
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(500)] public string? ReviewReason { get; set; }
}
