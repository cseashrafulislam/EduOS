using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    /// <summary>
    /// Read-only Finance preflight for issues #45/#47/#48/#51.
    /// It never authorizes a monetary change: Operations must recheck posted activity
    /// inside its own mutation transaction, and must block charged changes until an
    /// approved, persisted Finance adjustment and billing provenance contract exists.
    /// </summary>
    public async Task<ApiResponse<ReservationFinancePreflightDto>> GetReservationFinancePreflightAsync(
        string kind, long id, CancellationToken ct = default)
    {
        if (!Authorized)
            return Error<ReservationFinancePreflightDto>("Institution administrator permission required.", 403);
        if (id <= 0 || string.IsNullOrWhiteSpace(kind))
            return Error<ReservationFinancePreflightDto>("Reservation type and ID are required.");
        var type = kind.Trim().ToLowerInvariant();
        if (type != "hostel" && type != "transport")
            return Error<ReservationFinancePreflightDto>("Reservation type must be Hostel or Transport.");

        var tenant = user.TenantId;
        long studentId, enrollmentId;
        long? reservationCampus = null;
        DateOnly start;
        DateOnly? end;
        decimal charge, deposit = 0m;
        bool hasFeeHead = false;

        if (type == "hostel")
        {
            var stay = await db.StudentHostelAllocations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted && x.Id == id, ct);
            if (stay == null) return Error<ReservationFinancePreflightDto>("Reservation not found.", 404);
            reservationCampus = await (
                from bed in db.HostelBeds.AsNoTracking()
                join room in db.HostelRooms.AsNoTracking()
                    on new { bed.TenantId, Id = bed.HostelRoomId } equals new { room.TenantId, room.Id }
                join hostel in db.Hostels.AsNoTracking()
                    on new { room.TenantId, Id = room.HostelId } equals new { hostel.TenantId, hostel.Id }
                where bed.TenantId == tenant && !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted
                    && bed.Id == stay.HostelBedId
                select (long?)hostel.CampusId).FirstOrDefaultAsync(ct);
            studentId = stay.StudentId;
            enrollmentId = stay.StudentEnrollmentId;
            start = stay.StartDate;
            end = stay.EndDate;
            charge = stay.MonthlyRent;
            deposit = stay.SecurityDeposit;
            hasFeeHead = stay.FeeHeadId.HasValue;
        }
        else
        {
            var ride = await db.StudentTransports.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted && x.Id == id, ct);
            if (ride == null) return Error<ReservationFinancePreflightDto>("Reservation not found.", 404);
            studentId = ride.StudentId;
            enrollmentId = ride.StudentEnrollmentId;
            start = ride.StartDate;
            end = ride.EndDate;
            charge = ride.MonthlyFare;
        }

        var enrollment = await db.StudentEnrollments.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.Id == enrollmentId
                && x.StudentId == studentId)
            .Select(x => new { x.CampusId }).FirstOrDefaultAsync(ct);
        if (enrollment == null || (type == "hostel" && reservationCampus != enrollment.CampusId))
            return Error<ReservationFinancePreflightDto>("Reservation enrollment or campus linkage is invalid.", 409);

        // No canonical reservation-to-invoice source FK exists. Conservatively include
        // ALL posted activity for the enrollment, and ALL successful student payments.
        var postedInvoice = await db.StudentInvoices.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.StudentEnrollmentId == enrollmentId &&
            (x.JournalId.HasValue || x.PaidAmount > 0m ||
             x.State == InvoiceState.PartiallyPaid || x.State == InvoiceState.Paid ||
             x.State == InvoiceState.Refunded), ct);
        var postedPayment = await db.StudentPayments.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.StudentId == studentId &&
            (x.JournalId.HasValue || x.State == PaymentState.Successful ||
             x.State == PaymentState.Refunded), ct);
        var issuedUnposted = await db.StudentInvoices.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.StudentEnrollmentId == enrollmentId &&
            x.State == InvoiceState.Issued && !x.JournalId.HasValue && x.PaidAmount == 0m, ct);
        var posted = postedInvoice || postedPayment;
        var charged = charge != 0m || deposit != 0m || hasFeeHead;
        var decision = posted
            ? "BLOCKED: Posted invoice/payment activity requires a separately approved, durable Finance adjustment before any financially effective reservation change."
            : charged
                ? "BLOCKED: Chargeable reservation requires Finance billing-period, deposit and proration policy before a financially effective change."
                : issuedUnposted
                    ? "BLOCKED: An issued invoice exists; reconcile billing before changing the reservation."
                    : "No posted activity or charges detected in this snapshot. This is not approval: Operations must revalidate inside its write transaction.";

        return ApiResponse<ReservationFinancePreflightDto>.SuccessResponse(new ReservationFinancePreflightDto
        {
            Kind = type == "hostel" ? "Hostel" : "Transport",
            ReservationId = id,
            CampusId = enrollment.CampusId,
            StudentId = studentId,
            StudentEnrollmentId = enrollmentId,
            StartDate = start,
            EndDate = end,
            MonthlyCharge = charge,
            SecurityDeposit = deposit,
            HasFeeHead = hasFeeHead,
            HasPostedInvoiceActivity = postedInvoice,
            HasPostedPaymentActivity = postedPayment,
            HasUnpostedIssuedInvoice = issuedUnposted,
            RequiresApprovedFinanceAdjustment = posted,
            RequiresBillingPolicy = charged || issuedUnposted,
            FinancialMutationApproved = false,
            Decision = decision
        });
    }
}
