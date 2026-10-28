using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

public sealed class BankReconciliationSaveRequestDto
{
    public Guid ClientRequestId { get; set; }
    public string StatementIdentifier { get; set; } = string.Empty;
    public BankStatementPreviewRequestDto Statement { get; set; } = new();
}

public sealed class BankReconciliationStatementLineDto
{
    public int RowNumber { get; set; }
    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public decimal Deposit { get; set; }
    public decimal Withdrawal { get; set; }
}

public sealed class BankReconciliationBatchDto
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string StatementIdentifier { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public int MovementCount { get; set; }
    public int StateCode { get; set; }
    public long PreparedByUserId { get; set; }
    public DateTime PreparedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<BankReconciliationStatementLineDto> Movements { get; set; } =
        Array.Empty<BankReconciliationStatementLineDto>();
}
