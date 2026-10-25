using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingDriverChangeTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Future_driver_reservation_accepts_license_valid_through_inclusive_end() =>
        Assert.True(TransportService.CanChangeUpcomingDriverDates(
            Today.AddDays(1), Today.AddDays(30), Today.AddDays(30), Today));

    [Theory]
    [InlineData(0, 30, 40)]
    [InlineData(-1, 30, 40)]
    [InlineData(10, 9, 40)]
    [InlineData(10, 30, 29)]
    public void Operating_invalid_or_expired_license_periods_are_rejected(
        int fromDays, int endDays, int licenseDays) =>
        Assert.False(TransportService.CanChangeUpcomingDriverDates(
            Today.AddDays(fromDays), Today.AddDays(endDays), Today.AddDays(licenseDays), Today));

    [Fact]
    public void Open_ended_reservation_is_not_eligible() =>
        Assert.False(TransportService.CanChangeUpcomingDriverDates(
            Today.AddDays(2), null, Today.AddDays(100), Today));

    [Fact]
    public void Missing_license_expiry_is_not_eligible() =>
        Assert.False(TransportService.CanChangeUpcomingDriverDates(
            Today.AddDays(2), Today.AddDays(30), null, Today));
}
