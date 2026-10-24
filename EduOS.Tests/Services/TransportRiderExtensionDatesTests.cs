using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportRiderExtensionDatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    [Fact]
    public void Operating_rider_can_extend_current_finite_reservation()
    {
        Assert.True(TransportService.CanExtendOperatingRiderDates(
            Today.AddDays(-20), Today.AddDays(2), Today.AddDays(30), Today));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(2, 2)]
    [InlineData(2, 1)]
    public void Expired_or_non_extension_dates_are_rejected(int oldOffset, int newOffset)
    {
        Assert.False(TransportService.CanExtendOperatingRiderDates(
            Today.AddDays(-20), Today.AddDays(oldOffset), Today.AddDays(newOffset), Today));
    }

    [Fact]
    public void Upcoming_and_open_ended_reservations_are_not_extendable()
    {
        Assert.False(TransportService.CanExtendOperatingRiderDates(
            Today.AddDays(1), Today.AddDays(10), Today.AddDays(20), Today));
        Assert.False(TransportService.CanExtendOperatingRiderDates(
            Today.AddDays(-10), null, Today.AddDays(20), Today));
    }
}
