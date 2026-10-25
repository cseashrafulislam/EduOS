using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingDriverStartRescheduleTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Scheduled_driver_can_move_earlier_with_valid_license() =>
        Assert.True(TransportService.CanRescheduleUpcomingDriverDates(
            Today.AddDays(20), Today.AddDays(40), Today.AddDays(3), Today.AddDays(50), Today));

    [Fact]
    public void Scheduled_driver_can_move_later_through_inclusive_end() =>
        Assert.True(TransportService.CanRescheduleUpcomingDriverDates(
            Today.AddDays(5), Today.AddDays(30), Today.AddDays(30), Today.AddDays(30), Today));

    [Theory]
    [InlineData(0, 30, 5, 40)]
    [InlineData(5, 30, 0, 40)]
    [InlineData(5, 30, 31, 40)]
    [InlineData(5, 30, 10, 29)]
    [InlineData(5, 30, 366, 400)]
    public void Invalid_start_end_or_license_dates_are_rejected(
        int oldOffset, int endOffset, int newOffset, int licenseOffset) =>
        Assert.False(TransportService.CanRescheduleUpcomingDriverDates(
            Today.AddDays(oldOffset), Today.AddDays(endOffset),
            Today.AddDays(newOffset), Today.AddDays(licenseOffset), Today));

    [Fact]
    public void Open_ended_assignment_without_perpetual_license_is_rejected() =>
        Assert.False(TransportService.CanRescheduleUpcomingDriverDates(
            Today.AddDays(5), null, Today.AddDays(10), Today.AddDays(50), Today));

    [Fact]
    public void Missing_license_is_rejected() =>
        Assert.False(TransportService.CanRescheduleUpcomingDriverDates(
            Today.AddDays(5), Today.AddDays(30), Today.AddDays(10), null, Today));
}
