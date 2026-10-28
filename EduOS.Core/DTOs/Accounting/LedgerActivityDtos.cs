using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

// Posted journal activity only. Totals cover the full filtered period; Entries contains one SQL page.
public sealed class LedgerActivityPageDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }
    public PagedResult<LedgerActivityRowDto> Entries { get; set; } = new();
}

public sealed class LedgerActivityRowDto
{
    public DateOnly Date { get; set; }
    public Guid JournalReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public int LineNo { get; set; }
    public long? CampusId { get; set; }
    public string? SourceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Description { get; set; }
    public bool IsReversal { get; set; }
    public bool IsReversedOriginal { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
}
