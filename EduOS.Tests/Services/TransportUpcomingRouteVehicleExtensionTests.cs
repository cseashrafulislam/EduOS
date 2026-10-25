using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingRouteVehicleExtensionTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void Upcoming_finite_reservation_can_extend_its_end() =>
        Assert.True(TransportService.CanExtendUpcomingRouteVehicleDates(
            Today.AddDays(5), Today.AddDays(15), Today.AddDays(30), Today));

    [Theory]
    [InlineData(0, 15, 20)]
    [InlineData(-1, 15, 20)]
    [InlineData(5, 15, 15)]
    [InlineData(5, 15, 14)]
    [InlineData(5, 3, 20)]
    public void Invalid_future_or_extension_boundaries_are_rejected(
        int startOffset, int currentEndOffset, int requestedEndOffset) =>
        Assert.False(TransportService.CanExtendUpcomingRouteVehicleDates(
            Today.AddDays(startOffset), Today.AddDays(currentEndOffset),
            Today.AddDays(requestedEndOffset), Today));

    [Fact]
    public void Open_ended_reservation_cannot_be_extended() =>
        Assert.False(TransportService.CanExtendUpcomingRouteVehicleDates(
            Today.AddDays(5), null, Today.AddDays(20), Today));
}
