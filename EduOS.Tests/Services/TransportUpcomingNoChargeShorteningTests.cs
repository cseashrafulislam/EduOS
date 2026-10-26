using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingNoChargeShorteningTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void Future_finite_reservation_can_shorten_to_start_date() =>
        Assert.True(TransportService.CanShortenUpcomingNoChargeRiderDates(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(5), Today));

    [Fact]
    public void Future_finite_reservation_can_shorten_to_day_before_end() =>
        Assert.True(TransportService.CanShortenUpcomingNoChargeRiderDates(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(19), Today));

    [Theory]
    [InlineData(0, 20, 10)]
    [InlineData(-1, 20, 10)]
    [InlineData(5, 20, 4)]
    [InlineData(5, 20, 20)]
    [InlineData(5, 20, 21)]
    [InlineData(5, 4, 5)]
    public void Invalid_or_non_shortening_periods_are_rejected(int start, int oldEnd, int requested) =>
        Assert.False(TransportService.CanShortenUpcomingNoChargeRiderDates(
            Today.AddDays(start), Today.AddDays(oldEnd), Today.AddDays(requested), Today));

    [Fact]
    public void Open_ended_reservation_is_rejected() =>
        Assert.False(TransportService.CanShortenUpcomingNoChargeRiderDates(
            Today.AddDays(5), null, Today.AddDays(10), Today));

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.01, false)]
    [InlineData(250, false)]
    public void Only_zero_monthly_fare_is_eligible(decimal fare, bool expected) =>
        Assert.Equal(expected, TransportService.IsNoChargeRiderFare(fare));
}
