using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportRouteVehicleExtensionDatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    [Fact]
    public void Operating_assignment_can_extend_beyond_its_existing_end()
    {
        Assert.True(TransportService.CanExtendOperatingRouteVehicleDates(
            Today.AddDays(-30), Today.AddDays(2), Today.AddDays(45), Today));
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(2, 2)]
    [InlineData(2, 1)]
    public void Expired_or_non_extension_dates_are_rejected(int currentEndOffset, int requestedOffset)
    {
        Assert.False(TransportService.CanExtendOperatingRouteVehicleDates(
            Today.AddDays(-30), Today.AddDays(currentEndOffset),
            Today.AddDays(requestedOffset), Today));
    }

    [Fact]
    public void Future_or_open_ended_assignments_cannot_use_operating_extension()
    {
        Assert.False(TransportService.CanExtendOperatingRouteVehicleDates(
            Today.AddDays(1), Today.AddDays(5), Today.AddDays(15), Today));
        Assert.False(TransportService.CanExtendOperatingRouteVehicleDates(
            Today.AddDays(-5), null, Today.AddDays(15), Today));
    }
}
