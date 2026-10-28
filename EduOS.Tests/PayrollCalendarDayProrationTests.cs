using EduOS.Service.Services.HR;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollCalendarDayProrationTests
{
    [Theory]
    [InlineData(2026, 2, 10, 28, 19)]
    [InlineData(2026, 10, 16, 31, 16)]
    public void Joining_mid_month_counts_inclusive_calendar_days(
        int year, int month, int joinDay, int periodEnd, int days)
    {
        var from = new DateOnly(year, month, 1);
        var end = new DateOnly(year, month, periodEnd);
        Assert.Equal(days, PayrollProration.ServiceDays(new DateOnly(year, month, joinDay), null, from, end));
    }

    [Fact]
    public void Departing_mid_month_does_not_pay_the_rest_of_month()
    {
        Assert.Equal(15, PayrollProration.ServiceDays(new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Theory]
    [InlineData(31000, 15, 31, 15000)]
    [InlineData(30000, 15, 30, 15000)]
    [InlineData(31000, 31, 31, 31000)]
    public void Explicit_calendar_day_policy_produces_correct_fixed_component_amount(
        int monthly, int days, int period, int expected)
    {
        Assert.Equal((decimal)expected, PayrollProration.FixedAmount(monthly, days, period, "CalendarDays"));
    }

    [Fact]
    public void Legacy_full_month_policy_rejects_partial_employment()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PayrollProration.FixedAmount(31000m, 15, 31, "FullMonthOnly"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PayrollProration.FixedAmount(31000m, 31, 31, "Other"));
    }
}
