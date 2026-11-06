using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

// Full-period totals are independent of the requested journal page.
public sealed class PostedDayBookDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public PagedResult<PostedDayBookRowDto> Journals { get; set; } = new();
}

public sealed class PostedDayBookRowDto
{
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public long? CampusId { get; set; }
    public string? SourceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public bool IsReversal { get; set; }
    public bool IsReversedOriginal { get; set; }
    public int LineCount { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
}

public sealed class ComparativeAccountRowDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public decimal CurrentAmount { get; set; }
    public decimal ComparisonAmount { get; set; }
    public decimal Variance { get; set; }
}

public sealed class ComparativeIncomeStatementDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public DateOnly ComparisonFrom { get; set; }
    public DateOnly ComparisonTo { get; set; }
    public IReadOnlyList<ComparativeAccountRowDto> Accounts { get; set; } = Array.Empty<ComparativeAccountRowDto>();
    public decimal CurrentIncome { get; set; }
    public decimal ComparisonIncome { get; set; }
    public decimal CurrentExpenses { get; set; }
    public decimal ComparisonExpenses { get; set; }
    public decimal CurrentNetSurplus { get; set; }
    public decimal ComparisonNetSurplus { get; set; }
}

public sealed class ComparativeBalanceSheetDto
{
    public DateOnly AsOfDate { get; set; }
    public DateOnly ComparisonAsOfDate { get; set; }
    public IReadOnlyList<ComparativeAccountRowDto> Accounts { get; set; } = Array.Empty<ComparativeAccountRowDto>();
    public decimal CurrentAssets { get; set; }
    public decimal ComparisonAssets { get; set; }
    public decimal CurrentLiabilities { get; set; }
    public decimal ComparisonLiabilities { get; set; }
    public decimal CurrentEquity { get; set; }
    public decimal ComparisonEquity { get; set; }
    public decimal CurrentUnclosedSurplus { get; set; }
    public decimal ComparisonUnclosedSurplus { get; set; }
}

public sealed class IncomeExpenseBreakdownDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string GroupBy { get; set; } = string.Empty;
    public IReadOnlyList<IncomeExpenseBreakdownRowDto> Rows { get; set; } = Array.Empty<IncomeExpenseBreakdownRowDto>();
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetSurplus { get; set; }
}

public sealed class IncomeExpenseBreakdownRowDto
{
    public string Group { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal NetSurplus { get; set; }
}

// Read-only posted GL drill-down. This is accrual income/expense, not the Cash Book.
public sealed class InstitutionalIncomeExpenseDetailPageDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Category { get; set; } = "all";
    public long? AccountId { get; set; }
    public string? SourceType { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetSurplus { get; set; }
    public PagedResult<InstitutionalIncomeExpenseLineDto> Entries { get; set; } = new();
}

public sealed class InstitutionalIncomeExpenseLineDto
{
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public long? CampusId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public int LineNo { get; set; }
    public string? Description { get; set; }
    public bool IsReversal { get; set; }
    public bool IsReversedOriginal { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal Amount { get; set; }
}

// One source module per page: this keeps SQL paging correct without loading
// different operational tables into memory or counting their money twice.
public sealed class FinanceSourcePostingAuditPageDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public int LinkedCount { get; set; }
    public int PendingPostingCount { get; set; }
    public int UnpostedCount { get; set; }
    // GL-dated posted journals whose source row no longer exists in this tenant.
    // Null means the specialized source audit does not compute this metric.
    public int? OrphanPostedJournalCount { get; set; }
    public PagedResult<FinanceSourcePostingAuditRowDto> Sources { get; set; } = new();
}

public sealed class FinanceSourcePostingAuditRowDto
{
    public long SourceId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public long? CampusId { get; set; }
    public string SourceState { get; set; } = string.Empty;
    public decimal? SourceAmount { get; set; }
    public long? JournalId { get; set; }
    public Guid? JournalReference { get; set; }
    public string? JournalNumber { get; set; }
    public string PostingStatus { get; set; } = string.Empty;
    public string? Finding { get; set; }
}
