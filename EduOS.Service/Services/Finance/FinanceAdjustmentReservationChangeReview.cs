using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Finance;

public sealed partial class FinanceAdjustmentService
{
    /// <summary>
    /// Finance-owned proposal review for #47/#48/#51. Reuses the canonical
    /// preflight's tenant/campus/resource checks and conservative billing evidence.
    /// Quotes reflect campus policy but never approve a change or post money.
    /// </summary>
    public async Task<ApiResponse<ReservationFinanceChangeReviewDto>> ReviewReservationChangeAsync(
        ReservationFinanceChangeReviewRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized)
            return Error<ReservationFinanceChangeReviewDto>("Institution administrator permission required.", 403);
        if (request == null || request.ReservationId <= 0 || string.IsNullOrWhiteSpace(request.Change))
            return Error<ReservationFinanceChangeReviewDto>("Reservation and proposed change are required.");

        var action = request.Change.Trim().ToLowerInvariant();
        if (action is not ("reschedule-start" or "extend-end" or "shorten-end"))
            return Error<ReservationFinanceChangeReviewDto>("Unsupported proposal; occupied transfers require the separate approved Finance contract.");

        var preflight = await GetReservationFinancePreflightAsync(request.Kind, request.ReservationId, ct);
        if (!preflight.Success || preflight.Data == null)
            return Error<ReservationFinanceChangeReviewDto>(preflight.Message, preflight.StatusCode);

        var current = preflight.Data;
        var start = current.StartDate;
        var end = current.EndDate;
        switch (action)
        {
            case "reschedule-start":
                if (!request.ProposedStartDate.HasValue || request.ProposedStartDate.Value == default ||
                    request.ProposedEndDate.HasValue || request.ProposedStartDate.Value == start)
                    return Error<ReservationFinanceChangeReviewDto>("Supply a different proposed start date only.");
                start = request.ProposedStartDate.Value;
                break;
            case "extend-end":
                if (!end.HasValue || !request.ProposedEndDate.HasValue ||
                    request.ProposedStartDate.HasValue || request.ProposedEndDate.Value <= end.Value)
                    return Error<ReservationFinanceChangeReviewDto>("Supply an end date later than the existing finite end date.");
                end = request.ProposedEndDate.Value;
                break;
            case "shorten-end":
                if (!end.HasValue || !request.ProposedEndDate.HasValue ||
                    request.ProposedStartDate.HasValue || request.ProposedEndDate.Value >= end.Value)
                    return Error<ReservationFinanceChangeReviewDto>("Supply an end date earlier than the existing finite end date.");
                end = request.ProposedEndDate.Value;
                break;
        }
        if (end.HasValue && end.Value < start)
            return Error<ReservationFinanceChangeReviewDto>("Proposed end date precedes start date.");
        if (current.Kind == "Hostel" && end.HasValue && end.Value.DayNumber - start.DayNumber > 89)
            return Error<ReservationFinanceChangeReviewDto>("Hostel fixed-term occupancy cannot exceed 90 inclusive days.");

        var hostelPolicy = current.Kind == "Hostel" ? await db.HostelBillingPolicies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.CampusId == current.CampusId && !x.IsDeleted)
            .Select(x => new { Mode = (int)x.CalculationMode, x.AllowManualOverride })
            .FirstOrDefaultAsync(ct) : null;
        var transportPolicy = current.Kind == "Transport" ? await db.TransportBillingPolicies.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.CampusId == current.CampusId && !x.IsDeleted)
            .Select(x => new { Mode = (int)x.CalculationMode, x.AllowManualOverride })
            .FirstOrDefaultAsync(ct) : null;
        var mode = hostelPolicy?.Mode ?? transportPolicy?.Mode ?? 1;
        var manualAllowed = mode == 3 || (hostelPolicy?.AllowManualOverride ??
            transportPolicy?.AllowManualOverride ?? true);
        var quote = ReservationTermQuoteCalculator.Calculate(current.StartDate, current.EndDate,
            start, end, current.MonthlyCharge, mode);
        ReservationQuoteSelection.Result selection;
        try
        {
            selection = ReservationQuoteSelection.Select(quote.NetAdjustment, mode == 3,
                manualAllowed, request.ManualNetAdjustment, request.ManualOverrideReason,
                request.ManualAdjustmentDelta);
        }
        catch (ArgumentException ex)
        {
            return Error<ReservationFinanceChangeReviewDto>(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Error<ReservationFinanceChangeReviewDto>(ex.Message, 409);
        }

        var posted = current.HasPostedInvoiceActivity || current.HasPostedPaymentActivity;
        var issued = current.HasUnpostedIssuedInvoice;
        var charged = current.MonthlyCharge != 0m || current.SecurityDeposit != 0m || current.HasFeeHead;
        var status = posted ? "BLOCKED_POSTED_ACTIVITY" :
            issued ? "BLOCKED_ISSUED_INVOICE" :
            charged ? "BLOCKED_CHARGED_TERMS" : "NO_KNOWN_EXPOSURE_NOT_APPROVED";
        var decision = posted
            ? "BLOCKED: Posted invoice/payment evidence requires a persisted, approved Finance adjustment and atomic Operations coordination."
            : issued
                ? "BLOCKED: Issued invoice must be reconciled before a reservation amendment."
                : charged
                    ? "BLOCKED: Charged reservation requires approved billing-period, deposit and proration policy."
                    : "No known Finance exposure in this snapshot. This is NOT approval; Operations must enforce its own rules and recheck inside the write transaction.";

        return ApiResponse<ReservationFinanceChangeReviewDto>.SuccessResponse(
            new ReservationFinanceChangeReviewDto
            {
                Kind = current.Kind,
                ReservationId = current.ReservationId,
                CampusId = current.CampusId,
                Change = action,
                CurrentStartDate = current.StartDate,
                CurrentEndDate = current.EndDate,
                ProposedStartDate = start,
                ProposedEndDate = end,
                MonthlyCharge = current.MonthlyCharge,
                SecurityDeposit = current.SecurityDeposit,
                CalculationMode = mode == 1 ? "DailyProrated" : mode == 2 ? "FixedMonthly" : "Manual",
                OriginalPeriodEstimate = quote.OriginalAmount,
                ProposedPeriodEstimate = quote.ProposedAmount,
                SuggestedNetAdjustment = quote.NetAdjustment,
                SelectedNetAdjustment = selection.SelectedAmount,
                ManualOverrideAllowed = manualAllowed,
                ManualOverrideApplied = selection.IsManual,
                AdjustmentSelectionMode = selection.Mode,
                QuoteNeedsManualReview = quote.NeedsManualReview,
                QuoteBasis = quote.Basis,
                HasPostedInvoiceActivity = current.HasPostedInvoiceActivity,
                HasPostedPaymentActivity = current.HasPostedPaymentActivity,
                HasUnpostedIssuedInvoice = issued,
                RequiresApprovedFinanceAdjustment = posted,
                RequiresBillingPolicy = charged || issued,
                FinanceExposureBlocksChange = posted || issued || charged,
                FinancialMutationApproved = false,
                Status = status,
                Decision = decision
            }, "Read-only proposed amendment reviewed; no financial approval or posting.");
    }
}
