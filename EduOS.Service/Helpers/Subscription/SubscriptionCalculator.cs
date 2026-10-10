using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;

namespace EduOS.Service.Helpers.Subscription;

public static class SubscriptionCalculator
{
    public static bool IsSupportedCycle(string? code) =>
        code is "Monthly" or "Quarterly" or "HalfYearly" or "Yearly" or "Lifetime";

    public static decimal GetPriceForCycle(SubscriptionPlan plan, string billingCycleCode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return billingCycleCode switch
        {
            "Monthly" => plan.MonthlyPrice,
            "Quarterly" => plan.MonthlyPrice * 3m,
            "HalfYearly" => plan.MonthlyPrice * 6m,
            "Yearly" => plan.YearlyPrice,
            "Lifetime" => plan.YearlyPrice * 5m,
            _ => throw new ArgumentOutOfRangeException(nameof(billingCycleCode))
        };
    }

    public static DateTime CalculateEndDate(DateTime startUtc, string billingCycleCode) => billingCycleCode switch
    {
        "Monthly" => startUtc.AddMonths(1),
        "Quarterly" => startUtc.AddMonths(3),
        "HalfYearly" => startUtc.AddMonths(6),
        "Yearly" => startUtc.AddYears(1),
        "Lifetime" => startUtc.AddYears(99),
        _ => throw new ArgumentOutOfRangeException(nameof(billingCycleCode))
    };

    public static DateTime CalculateTrialEndDate(DateTime startUtc, int trialDays)
    {
        if (trialDays <= 0) throw new ArgumentOutOfRangeException(nameof(trialDays));
        return startUtc.AddDays(trialDays);
    }

    public static int CalculateDaysRemaining(DateTime endUtc, DateTime nowUtc) =>
        Math.Max(0, (int)Math.Ceiling((endUtc - nowUtc).TotalDays));

    public static bool IsActive(DateTime endUtc, SubscriptionState state, DateTime nowUtc) =>
        endUtc > nowUtc && state is SubscriptionState.Active or SubscriptionState.Trial;
}
