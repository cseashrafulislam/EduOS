using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Accounting;

// Append-only proposal history; never a bank clearance or accounting journal.
public sealed class BankReconciliationMatchEvent : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long BankReconciliationBatchId { get; set; }
    public int ActionCode { get; set; } // 1 confirm exact match; 2 withdraw pending match
    public long? OriginalMatchEventId { get; set; }
    public int StatementRowNumber { get; set; }
    public long JournalLineId { get; set; }
    [Required, MaxLength(64)] public string JournalLineVersion { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    [MaxLength(150)] public string? Reference { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Deposit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Withdrawal { get; set; }
    [MaxLength(250)] public string? Reason { get; set; }
    public long ActorUserId { get; set; }
    public DateTime EventAt { get; set; } = DateTime.UtcNow;
}
