using EduOS.Core.DTOs.Finance;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Splits each frozen monthly fee head across positive instalment months.
/// Uses integer cents and largest remainders so no fee head gains or loses money.
/// This changes gross due timing only; discounts remain calculated at invoice issue.
/// </summary>
public static class FeeInstallmentDistribution
{
    public static bool ValidWeights(int months, IReadOnlyList<int>? weights) =>
        months is >= 1 and <= 12 && (weights == null ||
            (weights.Count == months && weights.All(x => x is >= 1 and <= 10000) &&
             weights.Sum() == 10000));

    public static decimal[][]? Allocate(IReadOnlyList<decimal> monthlyHeadAmounts, int months,
        IReadOnlyList<int>? weights)
    {
        if (!ValidWeights(months, weights) || monthlyHeadAmounts.Count is < 1 or > 50 ||
            monthlyHeadAmounts.Any(x => x <= 0m || x > 1000000000m ||
                decimal.Round(x, 2) != x)) return null;
        var result = Enumerable.Range(0, months)
            .Select(_ => new decimal[monthlyHeadAmounts.Count]).ToArray();
        for (var h = 0; h < monthlyHeadAmounts.Count; h++)
        {
            if (weights == null)
            {
                for (var m = 0; m < months; m++) result[m][h] = monthlyHeadAmounts[h];
                continue;
            }
            var cents = decimal.ToInt64(monthlyHeadAmounts[h] * months * 100m);
            var allocated = new long[months];
            var remainder = new long[months];
            for (var m = 0; m < months; m++)
            {
                var numerator = cents * weights[m];
                allocated[m] = numerator / 10000;
                remainder[m] = numerator % 10000;
            }
            var left = cents - allocated.Sum();
            var order = Enumerable.Range(0, months)
                .OrderByDescending(m => remainder[m]).ThenBy(m => m).ToArray();
            for (var i = 0; i < left; i++) allocated[order[i]]++;
            if (allocated.Any(x => x == 0)) return null;
            for (var m = 0; m < months; m++) result[m][h] = allocated[m] / 100m;
        }
        return result;
    }

    // Replayed request keys must match the frozen financial schedule, not merely
    // the student/structure/month metadata. This also covers historic equal plans.
    public static bool Matches(StudentFeeInstallmentPlanDto plan, IReadOnlyList<int>? weights)
    {
        var months = (plan.EndMonth.Year - plan.StartMonth.Year) * 12 +
            plan.EndMonth.Month - plan.StartMonth.Month + 1;
        if (!ValidWeights(months, weights) || plan.Lines.Count != plan.LineCount ||
            plan.Lines.Count == 0) return false;
        var heads = plan.Lines.GroupBy(x => x.FeeHeadId).OrderBy(x => x.Key).ToArray();
        if (heads.Length is < 1 or > 50 || heads.Any(x => x.Count() != months)) return false;
        var amounts = heads.Select(x => x.Sum(y => y.GrossAmount) / months).ToArray();
        var expected = Allocate(amounts, months, weights);
        if (expected == null) return false;
        for (var m = 0; m < months; m++)
        {
            var month = plan.StartMonth.AddMonths(m);
            for (var h = 0; h < heads.Length; h++)
                if (heads[h].Count(x => x.MonthStart == month &&
                    x.GrossAmount == expected[m][h]) != 1) return false;
        }
        return true;
    }
}
