using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

// A confirmed draft match is evidence only, never bank clearance or journal posting.
public sealed class BankReconciliationMatchCommandDto
{
    public Guid ClientRequestId { get; set; }
    public long BatchId { get; set; }
    public string BatchRowVersion { get; set; } = string.Empty;
    public int ActionCode { get; set; } // 1 confirm exact match, 2 withdraw pending match
    public int StatementRowNumber { get; set; }
    public long JournalLineId { get; set; }
    public string JournalLineVersion { get; set; } = string.Empty;
    public long? OriginalMatchEventId { get; set; }
    public string? Reason { get; set; }
}

public sealed class BankReconciliationMatchEventDto
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public Guid ClientRequestId { get; set; }
    public int ActionCode { get; set; }
    public long? OriginalMatchEventId { get; set; }
    public int StatementRowNumber { get; set; }
    public long JournalLineId { get; set; }
    public string JournalLineVersion { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public decimal Deposit { get; set; }
    public decimal Withdrawal { get; set; }
    public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public DateTime EventAt { get; set; }
    public bool IsWithdrawn { get; set; }
}
