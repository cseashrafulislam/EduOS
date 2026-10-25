using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingDriverExtensionTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact]
    public void Upcoming_reservation_can_extend_to_inclusive_license_expiry() =>
        Assert.True(TransportService.CanExtendUpcomingDriverDates(
            Today.AddDays(5), Today.AddDays(15), Today.AddDays(30), Today.AddDays(30), Today));

    [Theory]
    [InlineData(0, 15, 20, 30)]
    [InlineData(5, 15, 15, 30)]
    [InlineData(5, 15, 14, 30)]
    [InlineData(5, 15, 31, 30)]
    [InlineData(5, 3, 20, 30)]
    public void Invalid_reservation_extension_dates_are_rejected(
        int startOffset, int currentEndOffset, int requestedEndOffset, int licenseOffset) =>
        Assert.False(TransportService.CanExtendUpcomingDriverDates(
            Today.AddDays(startOffset), Today.AddDays(currentEndOffset),
            Today.AddDays(requestedEndOffset), Today.AddDays(licenseOffset), Today));

    [Fact]
    public void Open_ended_reservation_cannot_be_extended() =>
        Assert.False(TransportService.CanExtendUpcomingDriverDates(
            Today.AddDays(5), null, Today.AddDays(20), Today.AddDays(30), Today));

    [Fact]
    public void Missing_license_expiry_blocks_extension() =>
        Assert.False(TransportService.CanExtendUpcomingDriverDates(
            Today.AddDays(5), Today.AddDays(15), Today.AddDays(20), null, Today));
}
