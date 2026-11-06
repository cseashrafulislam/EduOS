using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

// Read-only account breakdown of the canonical posted GL. Whole-period totals
// stay independent of category/page filters and account cardinality.
public sealed class IncomeStatementPageDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public string Category { get; set; } = "all";
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetSurplus { get; set; }
    public PagedResult<IncomeStatementAccountRowDto> Accounts { get; set; } = new();
}

public sealed class IncomeStatementAccountRowDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    // Signed natural balance includes returns, reversals and contra postings.
    public decimal Amount { get; set; }
}
