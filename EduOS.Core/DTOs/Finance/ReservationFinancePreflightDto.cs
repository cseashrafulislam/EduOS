namespace EduOS.Core.DTOs.Finance;

/// <summary>
/// Conservative Finance read-only snapshot for a canonical Hostel/Transport reservation.
/// This is NOT a billing approval or a permission to change a reservation.
/// </summary>
public sealed class ReservationFinancePreflightDto
{
    public string Kind { get; set; } = string.Empty;
    public long ReservationId { get; set; }
    public long CampusId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal MonthlyCharge { get; set; }
    public decimal SecurityDeposit { get; set; }
    public bool HasFeeHead { get; set; }
    public bool HasPostedInvoiceActivity { get; set; }
    public bool HasPostedPaymentActivity { get; set; }
    public bool HasUnpostedIssuedInvoice { get; set; }
    public bool RequiresApprovedFinanceAdjustment { get; set; }
    public bool RequiresBillingPolicy { get; set; }
    public bool FinancialMutationApproved { get; set; } = false;
    public string Decision { get; set; } = string.Empty;
}
