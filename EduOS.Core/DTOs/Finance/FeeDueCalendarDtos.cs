using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

// Current issued-invoice obligations, NOT unissued fee plans or a historical as-of statement.
public sealed class FeeDueCalendarQueryDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public long? CampusId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class FeeDueCalendarMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal Outstanding { get; set; }
}

public sealed class FeeDueCalendarInvoiceDto
{
    public Guid InvoiceReference { get; set; }
    public Guid StudentReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
}

public sealed class FeeDueCalendarReportDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Basis { get; set; } = "Current unpaid amounts on issued invoices due within the selected dates; not unissued instalments, historical as-of balances or a cash forecast.";
    public IReadOnlyList<FeeReceivablesCampusDto> Campuses { get; set; } = Array.Empty<FeeReceivablesCampusDto>();
    public IReadOnlyList<FeeDueCalendarMonthDto> Months { get; set; } = Array.Empty<FeeDueCalendarMonthDto>();
    public PagedResult<FeeDueCalendarInvoiceDto> Invoices { get; set; } = new();
}
