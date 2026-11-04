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
