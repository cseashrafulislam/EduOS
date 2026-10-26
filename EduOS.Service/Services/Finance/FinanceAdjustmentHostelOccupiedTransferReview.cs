using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    /// <summary>
    /// Finance-side read-only occupied Hostel transfer review for issue #45.
    /// Never approve a transfer or infer an invoice/receipt source from enrollment-level evidence.
    /// </summary>
    public async Task<ApiResponse<HostelOccupiedTransferFinanceReviewDto>> ReviewHostelOccupiedTransferAsync(
        HostelOccupiedTransferFinanceReviewRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized)
            return Error<HostelOccupiedTransferFinanceReviewDto>("Institution administrator permission required.", 403);
        if (request == null || request.ReservationId <= 0 || request.DestinationBedId <= 0 ||
            request.EffectiveDate == default)
            return Error<HostelOccupiedTransferFinanceReviewDto>("Reservation, destination bed and effective date are required.");

        var preflight = await GetReservationFinancePreflightAsync("hostel", request.ReservationId, ct);
        if (!preflight.Success || preflight.Data == null)
            return Error<HostelOccupiedTransferFinanceReviewDto>(preflight.Message, preflight.StatusCode);

        var tenant = user.TenantId;
        var stay = await db.StudentHostelAllocations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted && x.Id == request.ReservationId, ct);
        if (stay == null)
            return Error<HostelOccupiedTransferFinanceReviewDto>("Reservation not found.", 404);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (stay.State != HostelAllocationState.Active || stay.StartDate > today ||
            stay.EndDate.HasValue && stay.EndDate.Value < today)
            return Error<HostelOccupiedTransferFinanceReviewDto>("Only a currently occupied active Hostel stay can be reviewed for transfer.", 409);
        if (request.EffectiveDate < today || request.EffectiveDate < stay.StartDate ||
            stay.EndDate.HasValue && request.EffectiveDate > stay.EndDate.Value)
            return Error<HostelOccupiedTransferFinanceReviewDto>("Effective date must be within the active stay and cannot be in the past.");
        if (stay.HostelBedId == request.DestinationBedId)
            return Error<HostelOccupiedTransferFinanceReviewDto>("Destination must differ from the current bed.");

        var destination = await (
            from bed in db.HostelBeds.AsNoTracking()
            join room in db.HostelRooms.AsNoTracking()
                on new { bed.TenantId, Id = bed.HostelRoomId } equals new { room.TenantId, room.Id }
            join hostel in db.Hostels.AsNoTracking()
                on new { room.TenantId, Id = room.HostelId } equals new { hostel.TenantId, hostel.Id }
            where bed.TenantId == tenant && bed.Id == request.DestinationBedId &&
                !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted
            select new
            {
                BedActive = bed.IsActive, RoomActive = room.IsActive,
                HostelActive = hostel.IsActive, hostel.CampusId, room.Capacity
            }).FirstOrDefaultAsync(ct);
        if (destination == null || !destination.BedActive || !destination.RoomActive ||
            !destination.HostelActive || destination.Capacity <= 0 ||
            destination.CampusId != preflight.Data.CampusId)
            return Error<HostelOccupiedTransferFinanceReviewDto>("Destination bed is unavailable or outside the enrollment campus.", 409);

        if (await db.StudentHostelAllocations.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.PreviousAllocationId == stay.Id, ct))
            return Error<HostelOccupiedTransferFinanceReviewDto>("Reservation already has a successor allocation.", 409);

        var currentEnd = stay.EndDate;
        if (await db.StudentHostelAllocations.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted &&
                x.HostelBedId == request.DestinationBedId &&
                x.State == HostelAllocationState.Active &&
                (!currentEnd.HasValue || x.StartDate <= currentEnd.Value) &&
                (!x.EndDate.HasValue || x.EndDate.Value >= request.EffectiveDate), ct))
            return Error<HostelOccupiedTransferFinanceReviewDto>("Destination bed has an overlapping active reservation.", 409);

        var evidence = preflight.Data;
        var posted = evidence.HasPostedInvoiceActivity || evidence.HasPostedPaymentActivity;
        var issued = evidence.HasUnpostedIssuedInvoice;
        var charged = evidence.MonthlyCharge != 0m || evidence.SecurityDeposit != 0m || evidence.HasFeeHead;
        var status = posted ? "BLOCKED_POSTED_ACTIVITY" :
            issued ? "BLOCKED_ISSUED_INVOICE" :
            charged ? "BLOCKED_CHARGED_TERMS" : "NO_KNOWN_EXPOSURE_NOT_APPROVED";
        var decision = posted
            ? "BLOCKED: Posted invoice/payment activity requires a durable, approved Finance adjustment and coordinated transfer."
            : issued
                ? "BLOCKED: Issued billing must be reconciled before an occupied transfer."
                : charged
                    ? "BLOCKED: Rent, fee-head and deposit transfer/proration policy has not been approved."
                    : "No known Finance exposure in this snapshot. This does not authorize an occupied transfer.";

        return ApiResponse<HostelOccupiedTransferFinanceReviewDto>.SuccessResponse(
            new HostelOccupiedTransferFinanceReviewDto
            {
                ReservationId = stay.Id, CampusId = evidence.CampusId,
                SourceBedId = stay.HostelBedId, DestinationBedId = request.DestinationBedId,
                EffectiveDate = request.EffectiveDate, CurrentStartDate = stay.StartDate,
                CurrentEndDate = stay.EndDate, MonthlyRent = stay.MonthlyRent,
                SecurityDeposit = stay.SecurityDeposit, HasFeeHead = evidence.HasFeeHead,
                HasPostedInvoiceActivity = evidence.HasPostedInvoiceActivity,
                HasPostedPaymentActivity = evidence.HasPostedPaymentActivity,
                HasUnpostedIssuedInvoice = issued, RequiresApprovedFinanceAdjustment = posted,
                RequiresBillingPolicy = issued || charged, FinancialMutationApproved = false,
                Status = status, Decision = decision
            }, "Read-only occupied transfer Finance review; no approval or posting.");
    }
}
