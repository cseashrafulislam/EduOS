using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportFutureDriverCoverageTests
{
    private static readonly DateOnly Day = new(2026, 11, 15);

    [Theory]
    [InlineData(true, 20, 12, 1, 1, true, "Ready")]
    [InlineData(true, 20, 0, 0, 0, false, "Idle")]
    [InlineData(true, 20, 21, 1, 1, true, "OverCapacity")]
    [InlineData(false, 20, 12, 1, 1, true, "InactiveVehicle")]
    [InlineData(true, 20, 12, 2, 1, true, "RouteConflict")]
    [InlineData(true, 20, 12, 0, 1, true, "NoRoute")]
    [InlineData(true, 20, 12, 1, 0, false, "NoDriver")]
    [InlineData(true, 20, 12, 1, 2, true, "DriverConflict")]
    [InlineData(true, 20, 12, 1, 1, false, "InactiveDriver")]
    public void Forecast_prioritizes_dispatch_risks(bool active, int capacity, int riders,
        int routes, int drivers, bool driverActive, string expected)
    {
        var result = TransportService.ClassifyFutureDriverCoverage(
            active, capacity, riders, routes, drivers, driverActive, Day.AddDays(1), Day);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Missing_license_expiry_is_not_ready()
    {
        Assert.Equal("MissingExpiry", TransportService.ClassifyFutureDriverCoverage(
            true, 20, 10, 1, 1, true, null, Day));
    }

    [Fact]
    public void License_expiry_is_inclusive_of_operating_day()
    {
        Assert.Equal("Ready", TransportService.ClassifyFutureDriverCoverage(
            true, 20, 10, 1, 1, true, Day, Day));
        Assert.Equal("Expired", TransportService.ClassifyFutureDriverCoverage(
            true, 20, 10, 1, 1, true, Day.AddDays(-1), Day));
    }
}
