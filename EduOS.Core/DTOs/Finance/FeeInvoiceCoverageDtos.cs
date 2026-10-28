using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

/// <summary>Coverage of current active enrolments, not a future instalment or proof of a specific fee scheme.</summary>
public sealed class FeeInvoiceCoverageQueryDto
{
    public long AcademicBatchId { get; set; }
    public DateOnly BillingMonth { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class FeeInvoiceCoverageRowDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public int IntegrityIssueCount { get; set; }
    public int LineIssueCount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class FeeInvoiceCoverageReportDto
{
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public DateOnly BillingMonth { get; set; }
    public int Eligible { get; set; }
    public int Unbilled { get; set; }
    public int Billed { get; set; }
    public int Review { get; set; }
    public string Basis { get; set; } =
        "Current active enrolments versus non-cancelled invoices dated in the selected month. " +
        "Line and header totals are checked; billed does not establish fee-structure compliance, posting or a future instalment agreement.";
    public PagedResult<FeeInvoiceCoverageRowDto> Students { get; set; } = new();
}
