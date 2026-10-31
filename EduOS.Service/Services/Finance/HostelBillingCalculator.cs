using EduOS.Core.Entities.Finance;

namespace EduOS.Service.Services.Finance;

/// <summary>Proposals only. Finance verifies the real billed/collected liability before approval.</summary>
public static class HostelBillingCalculator
{
    public static decimal ProrateUnusedMonth(decimal recurringAmount, DateOnly effectiveDate, DateOnly? originalEnd)
    {
        var last = new DateOnly(effectiveDate.Year, effectiveDate.Month,
            DateTime.DaysInMonth(effectiveDate.Year, effectiveDate.Month));
        if (originalEnd.HasValue && originalEnd.Value < last) last = originalEnd.Value;
        var remaining = Math.Max(0, last.DayNumber - effectiveDate.DayNumber);
        return decimal.Round(recurringAmount * remaining /
            DateTime.DaysInMonth(effectiveDate.Year, effectiveDate.Month), 2,
            MidpointRounding.AwayFromZero);
    }

    public static (decimal RentCredit, decimal DepositRefund) Suggest(
        HostelClearanceOperation operation, HostelBillingCalculationMode mode,
        decimal monthlyRent, decimal deposit, DateOnly effectiveDate, DateOnly? originalEnd)
    {
        if (operation == HostelClearanceOperation.Transfer) return (0m, 0m);
        var rent = 0m;
        if (mode == HostelBillingCalculationMode.DailyProrated)
            rent = ProrateUnusedMonth(monthlyRent, effectiveDate, originalEnd);
        return (rent, mode == HostelBillingCalculationMode.Manual ? 0m : deposit);
    }
}
