using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

/// <summary>
/// Read-only, enrollment/student-wide invoice or payment evidence for a validated
/// canonical reservation. These rows are NOT attributed to that reservation:
/// invoice/payment source provenance does not exist in the current schema.
/// </summary>
public sealed class ReservationFinanceEvidenceQueryDto
{
    public string Kind { get; set; } = "hostel";
    public long ReservationId { get; set; }
    public string Source { get; set; } = "invoice";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class ReservationFinanceEvidenceRowDto
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly ActivityDate { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? DueAmount { get; set; }
    public long? JournalId { get; set; }
    public bool HasPostedActivity { get; set; }
    public bool IsHistoricalSoftDeleted { get; set; }
    public bool IsUnpostedIssuedInvoice { get; set; }
}

public sealed class ReservationFinanceEvidenceDto
{
    public string Kind { get; set; } = string.Empty;
    public long ReservationId { get; set; }
    public long CampusId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public string Source { get; set; } = string.Empty;
    public PagedResult<ReservationFinanceEvidenceRowDto> Evidence { get; set; } = new();
    public bool FinancialMutationApproved { get; set; } = false;
    public string Notice { get; set; } =
        "Evidence is enrollment-wide for invoices and student-wide for payments. " +
        "Legacy soft-deleted posted history is shown for reconciliation. " +
        "No invoice or payment is proven to originate from this reservation; this is not Finance approval.";
}
