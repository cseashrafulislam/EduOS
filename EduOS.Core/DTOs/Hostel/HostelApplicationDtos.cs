using EduOS.Core.Entities.Hostel;

namespace EduOS.Core.DTOs.Hostel;

public sealed class SubmitHostelApplicationRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid HostelReference { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class HostelApplicationDecisionRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(500)] public string? Reason { get; set; }
    public long? OfferedBedId { get; set; }
    [Range(1, 168)] public int? OfferHours { get; set; }
}

public sealed class HostelApplicationRowDto
{
    public long Id { get; set; }
    public Guid EnrollmentReference { get; set; }
    public long CampusId { get; set; }
    public long HostelId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public HostelApplicationState State { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public long? OfferedBedId { get; set; }
    public DateTime? OfferExpiresAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long? AllocationId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class HostelApplicationEventDto
{
    public HostelApplicationState? FromState { get; set; }
    public HostelApplicationState ToState { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
}

public sealed class HostelApplicationOptionDto
{
    public Guid EnrollmentReference { get; set; }
    public Guid HostelReference { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string HostelName { get; set; } = string.Empty;
}

public sealed class GrantHostelApplicationAuthorityRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid GuardianReference { get; set; }
    [Required, MaxLength(150)] public string EvidenceReference { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class HostelApplicationAuthorityDto
{
    public long Id { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string GuardianName { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class RevokeHostelApplicationAuthorityRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class HostelAuthorityGuardianOptionDto
{
    public Guid GuardianReference { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public string RelationCode { get; set; } = string.Empty;
}
