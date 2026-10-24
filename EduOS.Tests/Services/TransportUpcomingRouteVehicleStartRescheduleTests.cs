using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingRouteVehicleStartRescheduleTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Future_start_can_move_earlier_without_crossing_today() =>
        Assert.True(TransportService.CanRescheduleUpcomingRouteVehicleDates(
            Today.AddDays(20), Today.AddDays(50), Today.AddDays(5), Today));

    [Fact]
    public void Future_start_can_move_later_within_end() =>
        Assert.True(TransportService.CanRescheduleUpcomingRouteVehicleDates(
            Today.AddDays(2), Today.AddDays(50), Today.AddDays(50), Today));

    [Fact]
    public void Open_ended_future_assignment_can_be_rescheduled() =>
        Assert.True(TransportService.CanRescheduleUpcomingRouteVehicleDates(
            Today.AddDays(3), null, Today.AddDays(10), Today));

    [Theory]
    [InlineData(-1, 5, 6)]
    [InlineData(0, 5, 6)]
    [InlineData(5, 5, 0)]
    [InlineData(5, 5, -1)]
    [InlineData(5, 10, 11)]
    [InlineData(5, 10, 366)]
    public void Operating_past_or_out_of_range_dates_are_rejected(
        int originalOffset, int endOffset, int proposedOffset) =>
        Assert.False(TransportService.CanRescheduleUpcomingRouteVehicleDates(
            Today.AddDays(originalOffset), Today.AddDays(endOffset),
            Today.AddDays(proposedOffset), Today));
}
