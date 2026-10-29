using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;

namespace EduOS.Service.Services.HR;

/// <summary>
/// Calculates tenant-approved withholding percentages; it does not infer tax law,
/// assessment year, allowances, rebates, caps, or mandatory PF rates.
/// Each employee's effective-dated salary structure is the approved rate snapshot.
/// </summary>
public static class PayrollStatutoryCalculation
{
    public sealed record CalculatedLine(long SalaryComponentId, decimal Amount, string Description);

    public static bool ValidLine(string code, SalaryComponentType type, string? method,
        decimal amount, decimal? percentage, long? basedOnId,
        IReadOnlyDictionary<long, SalaryComponentType> components)
    {
        if (amount < 0m || amount > 999999999999m || decimal.Round(amount, 2) != amount)
            return false;
        if (method is null or "" or "Manual" or "Fixed")
            return !percentage.HasValue && !basedOnId.HasValue;
        if (method != "Auto" || type != SalaryComponentType.Deduction ||
            code is not ("STAT_TAX" or "STAT_PF") || amount != 0m ||
            !percentage.HasValue || percentage.Value < 0m || percentage.Value > 100m ||
            decimal.Round(percentage.Value, 4) != percentage.Value)
            return false;
        if (code == "STAT_PF" && !basedOnId.HasValue)
            return false;
        return !basedOnId.HasValue ||
            components.TryGetValue(basedOnId.Value, out var baseType) &&
            baseType == SalaryComponentType.Earning;
    }

    public static bool TryCalculate(
        IReadOnlyList<SalaryStructureLine> lines,
        IReadOnlyDictionary<long, SalaryComponent> components,
        int serviceDays, int periodDays, string prorationModeCode,
        decimal taxableBonus,
        out IReadOnlyList<CalculatedLine> result)
    {
        result = Array.Empty<CalculatedLine>();
        var types = components.ToDictionary(x => x.Key, x => x.Value.Type);
        if (lines.Select(x => x.SalaryComponentId).Distinct().Count() != lines.Count ||
            lines.Any(x => !components.ContainsKey(x.SalaryComponentId) ||
            !ValidLine(components[x.SalaryComponentId].Code, types[x.SalaryComponentId],
                x.CalculationMethodCode, x.Amount, x.Percentage, x.BasedOnSalaryComponentId, types)))
            return false;

        var earnings = lines.Where(x => types[x.SalaryComponentId] == SalaryComponentType.Earning)
            .ToDictionary(x => x.SalaryComponentId, x => PayrollProration.FixedAmount(
                x.Amount, serviceDays, periodDays, prorationModeCode));
        if (lines.Any(x => x.CalculationMethodCode == "Auto" &&
            x.BasedOnSalaryComponentId.HasValue &&
            !earnings.ContainsKey(x.BasedOnSalaryComponentId.Value)) ||
            lines.Any(x => x.CalculationMethodCode == "Auto" &&
                components[x.SalaryComponentId].Code == "STAT_TAX" &&
                !x.BasedOnSalaryComponentId.HasValue &&
                !earnings.Keys.Any(id => components[id].IsTaxable)))
            return false;
        var taxableEarnings = earnings.Where(x => components[x.Key].IsTaxable).Sum(x => x.Value);
        var calculated = new List<CalculatedLine>(lines.Count);
        foreach (var line in lines)
        {
            var component = components[line.SalaryComponentId];
            if (line.CalculationMethodCode != "Auto")
            {
                calculated.Add(new CalculatedLine(line.SalaryComponentId,
                    PayrollProration.FixedAmount(line.Amount, serviceDays, periodDays, prorationModeCode),
                    "Manual"));
                continue;
            }
            var basis = line.BasedOnSalaryComponentId.HasValue
                ? earnings[line.BasedOnSalaryComponentId.Value]
                : taxableEarnings + taxableBonus;
            var value = Math.Round(basis * line.Percentage!.Value / 100m, 2,
                MidpointRounding.AwayFromZero);
            var basisName = line.BasedOnSalaryComponentId.HasValue
                ? components[line.BasedOnSalaryComponentId.Value].Code : "TAXABLE_EARNINGS";
            calculated.Add(new CalculatedLine(line.SalaryComponentId, value,
                $"Auto {line.Percentage.Value:0.####}% of {basisName}"));
        }
        result = calculated;
        return true;
    }
}
