using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingRouteVehicleChangeTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Upcoming_open_ended_assignment_can_change_vehicle() =>
        Assert.True(TransportService.CanChangeUpcomingRouteVehicleVehicleDates(
            Today.AddDays(1), null, Today));

    [Fact]
    public void Upcoming_fixed_term_assignment_can_change_vehicle() =>
        Assert.True(TransportService.CanChangeUpcomingRouteVehicleVehicleDates(
            Today.AddDays(1), Today.AddDays(1), Today));

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 5)]
    [InlineData(10, 9)]
    public void Started_or_invalid_period_is_not_eligible(int fromDays, int endDays) =>
        Assert.False(TransportService.CanChangeUpcomingRouteVehicleVehicleDates(
            Today.AddDays(fromDays), Today.AddDays(endDays), Today));
}
