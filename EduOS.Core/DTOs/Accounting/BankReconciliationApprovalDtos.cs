using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

public sealed class BankReconciliationApprovalCommandDto
{
    public Guid ClientRequestId { get; set; }
    public long BatchId { get; set; }
    public string BatchRowVersion { get; set; } = string.Empty;
    public int ActionCode { get; set; } // 2 submit, 3 independent approval, 4 request reversal, 5 approve reversal
    public string? Reason { get; set; }
}

public sealed class BankReconciliationApprovalEventDto
{
    public long Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public long BatchId { get; set; }
    public int ActionCode { get; set; }
    public string EvidenceFingerprint { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public DateTime EventAt { get; set; }
}

public sealed class BankReconciliationApprovalStatusDto
{
    public long BatchId { get; set; }
    public int StateCode { get; set; }
    public IReadOnlyList<BankReconciliationApprovalEventDto> Events { get; set; } =
        Array.Empty<BankReconciliationApprovalEventDto>();
}
