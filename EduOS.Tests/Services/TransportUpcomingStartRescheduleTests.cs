using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingStartRescheduleTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Upcoming_reservation_can_shift_earlier_or_later()
    {
        Assert.True(TransportService.CanRescheduleUpcomingRiderDates(
            Today.AddDays(10), Today.AddDays(30), Today.AddDays(2), Today));
        Assert.True(TransportService.CanRescheduleUpcomingRiderDates(
            Today.AddDays(10), null, Today.AddDays(20), Today));
    }

    [Theory]
    [InlineData(0, 2, 30)]
    [InlineData(-1, 2, 30)]
    [InlineData(10, 0, 30)]
    [InlineData(10, 31, 30)]
    public void Active_or_invalid_period_cannot_be_rescheduled(int current, int requested, int end)
    {
        Assert.False(TransportService.CanRescheduleUpcomingRiderDates(
            Today.AddDays(current), Today.AddDays(end), Today.AddDays(requested), Today));
    }

    [Fact]
    public void Inclusive_end_boundary_is_allowed()
    {
        Assert.True(TransportService.CanRescheduleUpcomingRiderDates(
            Today.AddDays(10), Today.AddDays(30), Today.AddDays(30), Today));
    }
}
