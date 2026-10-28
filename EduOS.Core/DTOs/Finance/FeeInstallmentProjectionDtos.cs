namespace EduOS.Core.DTOs.Finance;

/// <summary>Read-only, non-binding monthly fee projection; never creates instalments or invoices.</summary>
public sealed class FeeInstallmentProjectionQueryDto
{
    public long AcademicBatchId { get; set; }
    public long FeeStructureId { get; set; }
    public DateOnly StartMonth { get; set; }
    public int Months { get; set; } = 6;
}

public sealed class FeeInstallmentProjectionMonthDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly? EarliestConfiguredDueDate { get; set; }
    public DateOnly? LatestConfiguredDueDate { get; set; }
    public bool HasUnspecifiedDueDays { get; set; }
    public int FeeHeadCount { get; set; }
    public decimal GrossPerStudent { get; set; }
    public decimal GrossForCurrentCohort { get; set; }
    public int ObservedInvoiceCount { get; set; }
    public decimal ObservedInvoiceTotal { get; set; }
}

public sealed class FeeInstallmentProjectionReportDto
{
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public long FeeStructureId { get; set; }
    public string FeeStructureName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public int CurrentEligibleEnrollments { get; set; }
    public string Basis { get; set; } =
        "Non-binding gross projection for currently active enrolments using today's fee structure. " +
        "Excludes scholarships, discounts, fines, transfers and future enrolment changes. " +
        "Observed invoices are independent batch totals, not matched to this structure. " +
        "No instalments, invoices or accounting entries are created.";
    public IReadOnlyList<FeeInstallmentProjectionMonthDto> Months { get; set; } =
        Array.Empty<FeeInstallmentProjectionMonthDto>();
}
