using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    private sealed class ReservationWorklistCandidate
    {
        public long Id { get; set; }
        public long CampusId { get; set; }
        public long StudentId { get; set; }
        public long StudentEnrollmentId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public decimal MonthlyCharge { get; set; }
        public decimal SecurityDeposit { get; set; }
        public bool HasFeeHead { get; set; }
    }

    /// <summary>
    /// Paginated Finance discovery of canonical reservations for issues #45/#47/#48/#51.
    /// Campus comes from the tenant-owned enrollment. Resource linkage is verified by
    /// the existing per-reservation preflight, never assumed valid by this worklist.
    /// No approval, invoice mutation, GL posting or financial proration is performed.
    /// </summary>
    public async Task<ApiResponse<ReservationFinanceWorklistDto>> GetReservationFinanceWorklistAsync(
        ReservationFinanceWorklistQueryDto request, CancellationToken ct = default)
    {
        if (!Authorized)
            return Error<ReservationFinanceWorklistDto>("Institution administrator permission required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Kind) ||
            request.CampusId is <= 0 || request.Page is < 1 or > 10000 ||
            request.PageSize is < 1 or > 100 ||
            request.StartFrom.HasValue && request.StartFrom.Value == default ||
            request.StartTo.HasValue && request.StartTo.Value == default ||
            request.StartFrom.HasValue && request.StartTo.HasValue &&
            request.StartFrom.Value > request.StartTo.Value)
            return Error<ReservationFinanceWorklistDto>("Invalid reservation type, campus, dates or pagination.");

        var kind = request.Kind.Trim().ToLowerInvariant();
        if (kind is not ("hostel" or "transport"))
            return Error<ReservationFinanceWorklistDto>("Reservation type must be Hostel or Transport.");
        var tenant = user.TenantId;

        IQueryable<ReservationWorklistCandidate> query;
        if (kind == "hostel")
        {
            query = from stay in db.StudentHostelAllocations.AsNoTracking()
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on new { stay.TenantId, Id = stay.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                where stay.TenantId == tenant && !stay.IsDeleted &&
                    !enrollment.IsDeleted && stay.StudentId == enrollment.StudentId
                select new ReservationWorklistCandidate
                {
                    Id = stay.Id, CampusId = enrollment.CampusId,
                    StudentId = stay.StudentId, StudentEnrollmentId = stay.StudentEnrollmentId,
                    StartDate = stay.StartDate, EndDate = stay.EndDate,
                    MonthlyCharge = stay.MonthlyRent, SecurityDeposit = stay.SecurityDeposit,
                    HasFeeHead = stay.FeeHeadId.HasValue
                };
        }
        else
        {
            query = from ride in db.StudentTransports.AsNoTracking()
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on new { ride.TenantId, Id = ride.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                where ride.TenantId == tenant && !ride.IsDeleted &&
                    !enrollment.IsDeleted && ride.StudentId == enrollment.StudentId
                select new ReservationWorklistCandidate
                {
                    Id = ride.Id, CampusId = enrollment.CampusId,
                    StudentId = ride.StudentId, StudentEnrollmentId = ride.StudentEnrollmentId,
                    StartDate = ride.StartDate, EndDate = ride.EndDate,
                    MonthlyCharge = ride.MonthlyFare, SecurityDeposit = 0m, HasFeeHead = false
                };
        }

        if (request.CampusId.HasValue)
            query = query.Where(x => x.CampusId == request.CampusId.Value);
        if (request.StartFrom.HasValue)
            query = query.Where(x => x.StartDate >= request.StartFrom.Value);
        if (request.StartTo.HasValue)
            query = query.Where(x => x.StartDate <= request.StartTo.Value);

        // Filter BEFORE pagination. Correlated EXISTS stays in SQL and does not
        // pull the entire invoice/payment ledger into application memory.
        if (request.OnlyReviewRequired)
            query = query.Where(x =>
                x.MonthlyCharge != 0m || x.SecurityDeposit != 0m || x.HasFeeHead ||
                db.StudentInvoices.IgnoreQueryFilters().AsNoTracking().Any(i => i.TenantId == tenant &&
                    i.StudentEnrollmentId == x.StudentEnrollmentId &&
                    ((!i.IsDeleted && i.State == InvoiceState.Issued) || i.JournalId.HasValue ||
                     i.PaidAmount > 0m || i.State == InvoiceState.PartiallyPaid ||
                     i.State == InvoiceState.Paid || i.State == InvoiceState.Refunded)) ||
                db.StudentPayments.IgnoreQueryFilters().AsNoTracking().Any(p => p.TenantId == tenant &&
                    p.StudentId == x.StudentId &&
                    (p.JournalId.HasValue || p.State == PaymentState.Successful ||
                     p.State == PaymentState.Refunded)));

        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);

        var enrollmentIds = page.Select(x => x.StudentEnrollmentId).Distinct().ToArray();
        var studentIds = page.Select(x => x.StudentId).Distinct().ToArray();
        var invoiceFlags = await db.StudentInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant &&
                enrollmentIds.Contains(x.StudentEnrollmentId))
            .GroupBy(x => x.StudentEnrollmentId)
            .Select(g => new
            {
                EnrollmentId = g.Key,
                Posted = g.Any(x => x.JournalId.HasValue || x.PaidAmount > 0m ||
                    x.State == InvoiceState.PartiallyPaid || x.State == InvoiceState.Paid ||
                    x.State == InvoiceState.Refunded),
                Issued = g.Any(x => !x.IsDeleted && x.State == InvoiceState.Issued &&
                    !x.JournalId.HasValue && x.PaidAmount == 0m)
            }).ToListAsync(ct);
        var paymentFlags = await db.StudentPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant &&
                studentIds.Contains(x.StudentId))
            .GroupBy(x => x.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Posted = g.Any(x => x.JournalId.HasValue ||
                    x.State == PaymentState.Successful || x.State == PaymentState.Refunded)
            }).ToListAsync(ct);

        var invoicesByEnrollment = invoiceFlags.ToDictionary(x => x.EnrollmentId);
        var paymentsByStudent = paymentFlags.ToDictionary(x => x.StudentId);
        var rows = page.Select(x =>
        {
            var hasInvoice = invoicesByEnrollment.TryGetValue(x.StudentEnrollmentId, out var invoice);
            var hasPayment = paymentsByStudent.TryGetValue(x.StudentId, out var payment);
            var postedInvoice = hasInvoice && invoice!.Posted;
            var issuedInvoice = hasInvoice && invoice!.Issued;
            var postedPayment = hasPayment && payment!.Posted;
            return new ReservationFinanceWorklistRowDto
            {
                Kind = kind == "hostel" ? "Hostel" : "Transport",
                ReservationId = x.Id, CampusId = x.CampusId,
                StudentId = x.StudentId, StudentEnrollmentId = x.StudentEnrollmentId,
                StartDate = x.StartDate, EndDate = x.EndDate,
                MonthlyCharge = x.MonthlyCharge, SecurityDeposit = x.SecurityDeposit,
                HasFeeHead = x.HasFeeHead,
                HasPostedInvoiceActivity = postedInvoice,
                HasPostedPaymentActivity = postedPayment,
                HasUnpostedIssuedInvoice = issuedInvoice,
                RequiresFinanceReview = postedInvoice || postedPayment || issuedInvoice ||
                    x.MonthlyCharge != 0m || x.SecurityDeposit != 0m || x.HasFeeHead
            };
        }).ToList();

        return ApiResponse<ReservationFinanceWorklistDto>.SuccessResponse(
            new ReservationFinanceWorklistDto
            {
                Reservations = new PagedResult<ReservationFinanceWorklistRowDto>
                {
                    Page = request.Page, PageSize = request.PageSize,
                    TotalCount = total, Items = rows
                }
            }, "Read-only Finance reservation review queue; no financial mutation approved.");
    }
}
