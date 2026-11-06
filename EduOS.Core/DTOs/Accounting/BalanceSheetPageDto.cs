using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

public sealed class BalanceSheetPageDto
{
    public DateOnly AsOfDate { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public string Category { get; set; } = "all";
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
    public decimal UnclosedNetSurplus { get; set; }
    public decimal TotalLiabilitiesAndEquity { get; set; }
    public PagedResult<BalanceSheetRowDto> Accounts { get; set; } = new();
}
