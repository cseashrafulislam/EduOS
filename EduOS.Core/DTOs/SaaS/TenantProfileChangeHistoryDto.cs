namespace EduOS.Core.DTOs.SaaS;

/// <summary>Privacy-safe tenant configuration audit: field names, not private values.</summary>
public sealed class TenantProfileChangeHistoryDto
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ChangedFields { get; set; } = string.Empty;
    public long? ActorId { get; set; }
    public string? ActorName { get; set; }
    public DateTime OccurredAt { get; set; }
}
