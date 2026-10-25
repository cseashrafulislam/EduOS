using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingRiderExtensionDatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void Future_finite_rider_reservation_can_extend() =>
        Assert.True(TransportService.CanExtendUpcomingRiderDates(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(40), Today));

    [Theory]
    [InlineData(0, 20, 40)]
    [InlineData(-1, 20, 40)]
    [InlineData(5, 20, 20)]
    [InlineData(5, 20, 19)]
    [InlineData(5, 4, 40)]
    public void Invalid_periods_cannot_extend(int startOffset, int oldEndOffset, int newEndOffset) =>
        Assert.False(TransportService.CanExtendUpcomingRiderDates(
            Today.AddDays(startOffset), Today.AddDays(oldEndOffset),
            Today.AddDays(newEndOffset), Today));

    [Fact]
    public void Open_ended_reservation_cannot_extend() =>
        Assert.False(TransportService.CanExtendUpcomingRiderDates(
            Today.AddDays(5), null, Today.AddDays(40), Today));
}
