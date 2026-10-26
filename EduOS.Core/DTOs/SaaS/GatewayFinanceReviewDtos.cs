using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS;

/// <summary>Append-only finance triage; it does not authorize settlement, refund or invoice adjustment.</summary>
public sealed class GatewayFinanceReviewRequestDto
{
    [Required] public Guid RequestId { get; set; }
    [Required, MaxLength(200)] public string RowVersion { get; set; } = string.Empty;
    [Range(1, long.MaxValue)] public long EvidenceAuditId { get; set; }
    [Required, MaxLength(40)] public string Decision { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(500)] public string Note { get; set; } = string.Empty;
}

public sealed class GatewayFinanceHistoryItemDto
{
    public long AuditId { get; set; }
    public long PaymentId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long? ReviewerUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Decision { get; set; }
    public string? Note { get; set; }
    public long? EvidenceAuditId { get; set; }
    public string? EvidenceSha256 { get; set; }
    public bool ProviderConfirmed { get; set; }
    public bool RequiresManualReview { get; set; }
}
