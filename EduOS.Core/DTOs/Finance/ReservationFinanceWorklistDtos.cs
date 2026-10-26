using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

/// <summary>Tenant-scoped Finance review queue; it is never a reservation change authorization.</summary>
public sealed class ReservationFinanceWorklistQueryDto
{
    public string Kind { get; set; } = "hostel";
    public long? CampusId { get; set; }
    public DateOnly? StartFrom { get; set; }
    public DateOnly? StartTo { get; set; }
    public bool OnlyReviewRequired { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class ReservationFinanceWorklistRowDto
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
    public bool RequiresFinanceReview { get; set; }
    public bool FinancialMutationApproved { get; set; } = false;
}

public sealed class ReservationFinanceWorklistDto
{
    public PagedResult<ReservationFinanceWorklistRowDto> Reservations { get; set; } = new();
    public bool FinancialMutationApproved { get; set; } = false;
    public string Notice { get; set; } =
        "Enrollment/student-wide financial exposure only. Inspect each reservation's preflight; this list never authorizes a mutation.";
}
