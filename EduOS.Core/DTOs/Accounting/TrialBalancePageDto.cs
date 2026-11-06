using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Accounting;

public sealed class TrialBalancePageDto
{
    public DateOnly AsOfDate { get; set; }
    public decimal TotalDebitBalance { get; set; }
    public decimal TotalCreditBalance { get; set; }
    public PagedResult<TrialBalanceRowDto> Accounts { get; set; } = new();
}
