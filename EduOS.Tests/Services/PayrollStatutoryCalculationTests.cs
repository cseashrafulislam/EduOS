using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.HR;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollStatutoryCalculationTests
{
    private static readonly Dictionary<long, SalaryComponent> Components = new()
    {
        [1] = new() { Id = 1, Code = "BASIC", Type = SalaryComponentType.Earning, IsTaxable = true },
        [2] = new() { Id = 2, Code = "HOUSE", Type = SalaryComponentType.Earning, IsTaxable = false },
        [3] = new() { Id = 3, Code = "STAT_TAX", Type = SalaryComponentType.Deduction },
        [4] = new() { Id = 4, Code = "STAT_PF", Type = SalaryComponentType.Deduction },
        [5] = new() { Id = 5, Code = "OTHER", Type = SalaryComponentType.Deduction }
    };

    [Fact]
    public void Auto_Tax_uses_taxable_earnings_plus_taxable_bonus_and_PF_uses_basic()
    {
        var lines = new[]
        {
            new SalaryStructureLine { SalaryComponentId = 1, Amount = 20000m },
            new SalaryStructureLine { SalaryComponentId = 2, Amount = 5000m },
            new SalaryStructureLine { SalaryComponentId = 3, CalculationMethodCode = "Auto", Percentage = 5m },
            new SalaryStructureLine { SalaryComponentId = 4, CalculationMethodCode = "Auto", Percentage = 8m, BasedOnSalaryComponentId = 1 },
            new SalaryStructureLine { SalaryComponentId = 5, CalculationMethodCode = "Fixed", Amount = 250m }
        };
        Assert.True(PayrollStatutoryCalculation.TryCalculate(lines, Components, 30, 30,
            "FullMonthOnly", 2000m, out var result));
        Assert.Equal(1100m, result.Single(x => x.SalaryComponentId == 3).Amount);
        Assert.Equal(1600m, result.Single(x => x.SalaryComponentId == 4).Amount);
        Assert.Equal(250m, result.Single(x => x.SalaryComponentId == 5).Amount);
        Assert.Contains("5%", result.Single(x => x.SalaryComponentId == 3).Description);
    }

    [Fact]
    public void Calendar_day_proration_occurs_before_auto_percentage_without_double_proration()
    {
        var lines = new[]
        {
            new SalaryStructureLine { SalaryComponentId = 1, Amount = 30000m },
            new SalaryStructureLine { SalaryComponentId = 4, CalculationMethodCode = "Auto", Percentage = 10m, BasedOnSalaryComponentId = 1 }
        };
        Assert.True(PayrollStatutoryCalculation.TryCalculate(lines, Components, 15, 30,
            "CalendarDays", 0m, out var result));
        Assert.Equal(15000m, result.Single(x => x.SalaryComponentId == 1).Amount);
        Assert.Equal(1500m, result.Single(x => x.SalaryComponentId == 4).Amount);
    }

    [Theory]
    [InlineData("STAT_PF", "Auto", null, 10, 0, false)]
    [InlineData("STAT_TAX", "Auto", null, 101, 0, false)]
    [InlineData("STAT_TAX", "Auto", null, 10, 50, false)]
    [InlineData("OTHER", "Auto", null, 10, 0, false)]
    [InlineData("STAT_TAX", "Manual", null, 10, 0, false)]
    [InlineData("STAT_TAX", "Manual", null, null, 100, true)]
    [InlineData("STAT_PF", "Auto", 1, 10, 0, true)]
    [InlineData("STAT_TAX", "Auto", null, 0, 0, true)]
    public void Invalid_or_valid_configuration_is_enforced(string code, string method,
        long? basedOn, int? rate, int amount, bool expected)
    {
        var types = Components.ToDictionary(x => x.Key, x => x.Value.Type);
        Assert.Equal(expected, PayrollStatutoryCalculation.ValidLine(code,
            SalaryComponentType.Deduction, method, amount, rate, basedOn, types));
    }

    [Fact]
    public void Cross_structure_or_non_earning_base_is_rejected()
    {
        var types = Components.ToDictionary(x => x.Key, x => x.Value.Type);
        Assert.False(PayrollStatutoryCalculation.ValidLine("STAT_PF", SalaryComponentType.Deduction,
            "Auto", 0m, 10m, 99, types));
        Assert.False(PayrollStatutoryCalculation.ValidLine("STAT_PF", SalaryComponentType.Deduction,
            "Auto", 0m, 10m, 3, types));
        var lines = new[]
        {
            new SalaryStructureLine { SalaryComponentId = 2, Amount = 1000m },
            new SalaryStructureLine { SalaryComponentId = 4, CalculationMethodCode = "Auto", Percentage = 10m, BasedOnSalaryComponentId = 1 }
        };
        Assert.False(PayrollStatutoryCalculation.TryCalculate(lines, Components, 30, 30,
            "FullMonthOnly", 0m, out _));
    }

    [Fact]
    public void Zero_manual_tax_is_a_supported_reviewed_exemption()
    {
        var lines = new[]
        {
            new SalaryStructureLine { SalaryComponentId = 1, Amount = 10000m },
            new SalaryStructureLine { SalaryComponentId = 3, Amount = 0m }
        };
        Assert.True(PayrollStatutoryCalculation.TryCalculate(lines, Components, 30, 30,
            "FullMonthOnly", 0m, out var result));
        Assert.Equal(0m, result.Single(x => x.SalaryComponentId == 3).Amount);
    }
}
