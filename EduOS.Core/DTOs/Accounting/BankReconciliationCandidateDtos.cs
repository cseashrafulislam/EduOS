namespace EduOS.Core.DTOs.Accounting;

// Posted bank-ledger candidates for explicit reconciliation review.
// This is not a clearance register and never mutates journals.
public sealed class BankReconciliationCandidateDto
{
    public long JournalLineId { get; set; }
    public long JournalId { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public string? SourceType { get; set; }
    public long? CampusId { get; set; }
    public decimal Deposit { get; set; }
    public decimal Withdrawal { get; set; }
    public string JournalLineVersion { get; set; } = string.Empty;
}
public sealed class BankReconciliationCandidatePageDto
{
    public long BankAccountId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string? ReferencePrefix { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public decimal TotalDeposits { get; set; }
    public decimal TotalWithdrawals { get; set; }
    public IReadOnlyList<BankReconciliationCandidateDto> Rows { get; set; } =
        Array.Empty<BankReconciliationCandidateDto>();
}
