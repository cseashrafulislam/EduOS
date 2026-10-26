using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingDriverShorteningTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void Future_finite_driver_reservation_can_shorten() =>
        Assert.True(TransportService.CanShortenUpcomingDriverDates(
            Today.AddDays(5), Today.AddDays(30), Today.AddDays(12), Today));

    [Fact]
    public void Shortening_to_original_start_is_valid() =>
        Assert.True(TransportService.CanShortenUpcomingDriverDates(
            Today.AddDays(5), Today.AddDays(30), Today.AddDays(5), Today));

    [Theory]
    [InlineData(0, 30, 12)]
    [InlineData(-1, 30, 12)]
    [InlineData(5, 30, 31)]
    [InlineData(5, 30, 30)]
    [InlineData(5, 30, 4)]
    [InlineData(5, 4, 5)]
    public void Invalid_or_started_reservation_is_rejected(int start, int oldEnd, int newEnd) =>
        Assert.False(TransportService.CanShortenUpcomingDriverDates(
            Today.AddDays(start), Today.AddDays(oldEnd), Today.AddDays(newEnd), Today));

    [Fact]
    public void Open_ended_reservation_cannot_shorten() =>
        Assert.False(TransportService.CanShortenUpcomingDriverDates(
            Today.AddDays(5), null, Today.AddDays(12), Today));
}
