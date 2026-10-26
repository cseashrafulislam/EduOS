namespace EduOS.Core.DTOs.Finance;

/// <summary>Read-only proposal for occupied Hostel transfer; never a posting instruction.</summary>
public sealed class HostelOccupiedTransferFinanceReviewRequestDto
{
    public long ReservationId { get; set; }
    public long DestinationBedId { get; set; }
    public DateOnly EffectiveDate { get; set; }
}

public sealed class HostelOccupiedTransferFinanceReviewDto
{
    public long ReservationId { get; set; }
    public long CampusId { get; set; }
    public long SourceBedId { get; set; }
    public long DestinationBedId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly CurrentStartDate { get; set; }
    public DateOnly? CurrentEndDate { get; set; }
    public decimal MonthlyRent { get; set; }
    public decimal SecurityDeposit { get; set; }
    public bool HasFeeHead { get; set; }
    public bool HasPostedInvoiceActivity { get; set; }
    public bool HasPostedPaymentActivity { get; set; }
    public bool HasUnpostedIssuedInvoice { get; set; }
    public bool RequiresApprovedFinanceAdjustment { get; set; }
    public bool RequiresBillingPolicy { get; set; }
    public bool FinancialMutationApproved { get; set; } = false;
    public string Status { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Notice { get; set; } =
        "Snapshot only: no reservation-specific invoice provenance, proration, deposit settlement, " +
        "capacity certification, financial approval or posting. Operations must revalidate atomically.";
}
