using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Accounting;

// Append-only, tenant-owned approval and compensating clearance evidence.
// The original statement, matching events and posted journals are never modified.
public sealed class BankReconciliationApprovalEvent : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long BankReconciliationBatchId { get; set; }
    public int ActionCode { get; set; } // 2 submitted, 3 cleared, 4 reversal requested, 5 reversed
    [Required, MaxLength(64)] public string EvidenceFingerprint { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    [MaxLength(250)] public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public DateTime EventAt { get; set; } = DateTime.UtcNow;
}
