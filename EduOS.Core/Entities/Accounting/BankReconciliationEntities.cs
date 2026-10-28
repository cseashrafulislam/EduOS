using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Accounting;

// Immutable statement intake evidence. A draft does not approve, clear or alter any journal.
public sealed class BankReconciliationBatch : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long BankAccountId { get; set; }
    [Required, MaxLength(100)] public string StatementIdentifier { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string SourceFingerprint { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OpeningBalance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingBalance { get; set; }
    public int MovementCount { get; set; }
    // 1 = immutable intake draft. Approval/clearance requires a separate audited transition.
    public int StateCode { get; set; } = 1;
    public long PreparedByUserId { get; set; }
    public DateTime PreparedAt { get; set; } = DateTime.UtcNow;
}

public sealed class BankReconciliationStatementLine : BaseTenantEntity
{
    public long BankReconciliationBatchId { get; set; }
    public int RowNumber { get; set; }
    public DateOnly Date { get; set; }
    [MaxLength(150)] public string? Reference { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Deposit { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Withdrawal { get; set; }
}
