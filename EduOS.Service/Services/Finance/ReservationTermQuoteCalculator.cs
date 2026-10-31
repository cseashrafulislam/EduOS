namespace EduOS.Service.Services.Finance;

/// <summary>
/// A non-posting estimate of the change in chargeable days/months. Positive delta is
/// an additional charge, negative delta is a possible credit. Finance must still
/// reconcile issued invoices, deposits, discounts, posted journals and collections.
/// </summary>
public static class ReservationTermQuoteCalculator
{
    public sealed record Quote(decimal? OriginalAmount, decimal? ProposedAmount,
        decimal? NetAdjustment, string Basis, bool NeedsManualReview);

    public static Quote Calculate(DateOnly originalStart, DateOnly? originalEnd,
        DateOnly proposedStart, DateOnly? proposedEnd, decimal monthlyCharge, int mode)
    {
        if (monthlyCharge < 0m || monthlyCharge > 9999999999999999.99m ||
            originalEnd.HasValue && originalEnd.Value < originalStart ||
            proposedEnd.HasValue && proposedEnd.Value < proposedStart)
            throw new ArgumentOutOfRangeException(nameof(monthlyCharge), "Invalid reservation terms.");
        if (mode is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == 3)
            return new Quote(null, null, null, "Manual campus policy: authorized Finance quotation required.", true);
        if (!originalEnd.HasValue || !proposedEnd.HasValue)
            return new Quote(null, null, null,
                "Open-ended reservation: finite billing-period evidence required before estimation.", true);
        if (originalEnd.Value.DayNumber - originalStart.DayNumber > 3653 ||
            proposedEnd.Value.DayNumber - proposedStart.DayNumber > 3653)
            return new Quote(null, null, null,
                "Long-term contract: obtain an explicit Finance billing schedule before quotation.", true);

        var original = PeriodAmount(originalStart, originalEnd.Value, monthlyCharge, mode);
        var proposed = PeriodAmount(proposedStart, proposedEnd.Value, monthlyCharge, mode);
        var delta = proposed - original;
        return new Quote(original, proposed, delta,
            mode == 1 ? "DailyProrated: inclusive occupied days / actual days in each calendar month." :
                "FixedMonthly: one monthly charge for each calendar month touched by the reservation.", false);
    }

    private static decimal PeriodAmount(DateOnly start, DateOnly end, decimal monthlyCharge, int mode)
    {
        var total = 0m;
        var month = new DateOnly(start.Year, start.Month, 1);
        var lastMonth = new DateOnly(end.Year, end.Month, 1);
        while (month <= lastMonth)
        {
            var monthEnd = new DateOnly(month.Year, month.Month,
                DateTime.DaysInMonth(month.Year, month.Month));
            var first = start > month ? start : month;
            var last = end < monthEnd ? end : monthEnd;
            var amount = mode == 2 ? monthlyCharge :
                monthlyCharge * (last.DayNumber - first.DayNumber + 1) /
                    DateTime.DaysInMonth(month.Year, month.Month);
            total += decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (month == lastMonth) break;
            month = month.AddMonths(1);
        }
        return total;
    }
}
