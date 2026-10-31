using EduOS.Core.Entities.Finance;
using EduOS.Service.Services.Finance;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelFinanceCalculationTests
{
    [Theory]
    [InlineData(2026, 1, 15, 3100, 1600)]
    [InlineData(2024, 2, 14, 2900, 1500)]
    [InlineData(2026, 4, 29, 3000, 100)]
    public void Daily_auto_uses_actual_month_length_and_excludes_checkout_day(
        int year, int month, int day, decimal rent, decimal expected)
    {
        var result = HostelBillingCalculator.Suggest(HostelClearanceOperation.Checkout,
            HostelBillingCalculationMode.DailyProrated, rent, 500m,
            new DateOnly(year, month, day), null);
        Assert.Equal(expected, result.RentCredit);
        Assert.Equal(500m, result.DepositRefund);
    }

    [Fact]
    public void Proration_respects_original_end_in_same_month()
    {
        var result = HostelBillingCalculator.Suggest(HostelClearanceOperation.Checkout,
            HostelBillingCalculationMode.DailyProrated, 3000m, 200m,
            new DateOnly(2026, 4, 20), new DateOnly(2026, 4, 22));
        Assert.Equal(200m, result.RentCredit);
        Assert.Equal(200m, result.DepositRefund);
    }

    [Fact]
    public void Fixed_monthly_never_automatically_credits_remaining_days()
    {
        var result = HostelBillingCalculator.Suggest(HostelClearanceOperation.Checkout,
            HostelBillingCalculationMode.FixedMonthly, 3000m, 200m,
            new DateOnly(2026, 4, 12), null);
        Assert.Equal(0m, result.RentCredit);
        Assert.Equal(200m, result.DepositRefund);
    }

    [Fact]
    public void Manual_mode_requires_explicit_review_and_auto_suggests_no_amount()
    {
        var result = HostelBillingCalculator.Suggest(HostelClearanceOperation.Checkout,
            HostelBillingCalculationMode.Manual, 3000m, 200m,
            new DateOnly(2026, 4, 12), null);
        Assert.Equal((0m, 0m), result);
    }

    [Theory]
    [InlineData(HostelBillingCalculationMode.DailyProrated)]
    [InlineData(HostelBillingCalculationMode.FixedMonthly)]
    [InlineData(HostelBillingCalculationMode.Manual)]
    public void Transfer_never_automatically_refunds_deposit_or_credits_rent(HostelBillingCalculationMode mode)
    {
        var result = HostelBillingCalculator.Suggest(HostelClearanceOperation.Transfer,
            mode, 3000m, 200m, new DateOnly(2026, 4, 12), null);
        Assert.Equal((0m, 0m), result);
    }
}
