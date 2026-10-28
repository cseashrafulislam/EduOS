namespace EduOS.Core.DTOs.Accounting;

// Read-only comparison; never records clearance or changes financial postings.
public sealed class BankStatementPreviewRequestDto
{
    public long BankAccountId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public IReadOnlyList<BankStatementMovementDto> Movements { get; set; } = Array.Empty<BankStatementMovementDto>();
}
public sealed class BankStatementMovementDto
{
    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public decimal Deposit { get; set; }
    public decimal Withdrawal { get; set; }
}
public sealed class BankStatementExceptionDto
{
    // Populated only for ledger-side exceptions; never a statement-supplied identity.
    public long? JournalLineId { get; set; }
    public string? JournalLineVersion { get; set; }
    // 1-based data-row ordinal in the submitted statement (excluding the CSV header).
    public int? StatementRowNumber { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string? JournalNumber { get; set; }
    public string? Reference { get; set; }
    public decimal Deposit { get; set; }
    public decimal Withdrawal { get; set; }
    public string Reason { get; set; } = string.Empty;
}
// Unique exact matches are review evidence, never clearance or posting.
public sealed class BankStatementMatchDto
{
    public long JournalLineId { get; set; }
    public string JournalLineVersion { get; set; } = string.Empty;
    public int StatementRowNumber { get; set; }
    public DateOnly Date { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public decimal Deposit { get; set; }
    public decimal Withdrawal { get; set; }
}
public sealed class BankStatementPreviewDto
{
    public string BankName { get; set; } = string.Empty;
    public decimal LedgerOpeningBalance { get; set; }
    public decimal LedgerClosingBalance { get; set; }
    public decimal StatementOpeningBalance { get; set; }
    public decimal StatementClosingBalance { get; set; }
    public decimal ClosingVariance { get; set; }
    // Variances are ledger minus statement. A zero closing variance alone is not proof of matching.
    public decimal OpeningVariance { get; set; }
    public decimal MovementVariance { get; set; }
    // Read-only evidence only; never signifies approval, clearance or posting.
    public bool IsExactMatch { get; set; }
    public int MatchedCount { get; set; }
    // Distinct normalized references repeated within either source; never auto-cleared.
    public int ReferenceCollisionCount { get; set; }
    public IReadOnlyList<BankStatementMatchDto> Matches { get; set; } = Array.Empty<BankStatementMatchDto>();
    public int UnmatchedLedgerCount { get; set; }
    public int UnmatchedStatementCount { get; set; }
    public IReadOnlyList<BankStatementExceptionDto> Exceptions { get; set; } = Array.Empty<BankStatementExceptionDto>();
}
