using EduOS.Core.Common;
namespace EduOS.Core.DTOs.Finance;
public sealed class FeeReceivablesAgingReportDto
{
    public DateOnly AgingDate { get; set; }
    public string Basis { get; set; } = "Current unpaid balances aged against the selected date; not historical GL.";
    public IReadOnlyList<FeeReceivablesCampusDto> Campuses { get; set; } = Array.Empty<FeeReceivablesCampusDto>();
    public IReadOnlyList<FeeReceivablesCurrencyDto> Totals { get; set; } = Array.Empty<FeeReceivablesCurrencyDto>();
    public PagedResult<FeeReceivablesInvoiceDto> Invoices { get; set; } = new();
}
