namespace EduOS.Service.Services.HR;

/// <summary>Explicit payroll period rule, no inferred public-holiday or unpaid-leave treatment.</summary>
public static class PayrollProration
{
    public static int ServiceDays(DateOnly joined, DateOnly? left, DateOnly start, DateOnly end)
    {
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        var from = joined > start ? joined : start;
        var to = left.HasValue && left.Value < end ? left.Value : end;
        return to < from ? 0 : to.DayNumber - from.DayNumber + 1;
    }

    public static decimal FixedAmount(decimal monthly, int serviceDays, int periodDays, string mode)
    {
        if (monthly < 0m || serviceDays <= 0 || periodDays <= 0 || serviceDays > periodDays)
            throw new ArgumentOutOfRangeException(nameof(serviceDays));
        if (mode == "FullMonthOnly")
        {
            if (serviceDays != periodDays)
                throw new InvalidOperationException("Partial-month service requires an explicit payroll policy.");
            return decimal.Round(monthly, 2, MidpointRounding.AwayFromZero);
        }
        if (mode != "CalendarDays") throw new ArgumentOutOfRangeException(nameof(mode));
        return decimal.Round(monthly * serviceDays / periodDays, 2, MidpointRounding.AwayFromZero);
    }
}
