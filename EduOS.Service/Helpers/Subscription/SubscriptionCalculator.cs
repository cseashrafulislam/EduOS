using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;

namespace EduOS.Service.Helpers.Subscription
{
    /// <summary>
    /// Pure functions for subscription price/period calculations.
    /// Keeps service code clean and unit-testable.
    /// </summary>
    public static class SubscriptionCalculator
    {
        /// <summary>
        /// Get the price for a given plan + billing cycle
        /// </summary>
        public static decimal GetPriceForCycle(SubscriptionPlan plan, BillingCycle cycle)
        {
            return cycle switch
            {
                BillingCycle.Monthly => plan.MonthlyPrice,
                BillingCycle.Quarterly => plan.QuarterlyPrice,
                BillingCycle.HalfYearly => plan.HalfYearlyPrice,
                BillingCycle.Yearly => plan.YearlyPrice,
                BillingCycle.Lifetime => plan.YearlyPrice * 5,
                _ => plan.MonthlyPrice
            };
        }

        /// <summary>
        /// Calculate end date based on start date + billing cycle
        /// </summary>
        public static DateTime CalculateEndDate(DateTime startDate, BillingCycle cycle)
        {
            return cycle switch
            {
                BillingCycle.Monthly => startDate.AddMonths(1),
                BillingCycle.Quarterly => startDate.AddMonths(3),
                BillingCycle.HalfYearly => startDate.AddMonths(6),
                BillingCycle.Yearly => startDate.AddYears(1),
                BillingCycle.Lifetime => startDate.AddYears(99),
                _ => startDate.AddMonths(1)
            };
        }

        /// <summary>
        /// Calculate trial end date for a trial plan
        /// </summary>
        public static DateTime CalculateTrialEndDate(DateTime startDate, int trialDays)
        {
            return startDate.AddDays(trialDays);
        }

        /// <summary>
        /// Days remaining until subscription expires (0 if already expired)
        /// </summary>
        public static int CalculateDaysRemaining(DateTime endDate)
        {
            var diff = (endDate - DateTime.UtcNow).TotalDays;
            return diff < 0 ? 0 : (int)Math.Ceiling(diff);
        }

        /// <summary>
        /// Whether subscription is currently active (not expired)
        /// </summary>
        public static bool IsActive(DateTime endDate, SubscriptionStatus status)
        {
            if (status == SubscriptionStatus.Active || status == SubscriptionStatus.Trialing)
            {
                return endDate > DateTime.UtcNow;
            }
            return false;
        }
    }
}
