using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportDriverCoverageWindowTests
{
    private static readonly DateOnly Start = new(2026, 11, 15);
    private static readonly (DateOnly Start, DateOnly? End)[] FullRiders = { (Start, Start.AddDays(2)) };
    private static readonly (DateOnly Start, DateOnly? End)[] FullRoutes = { (Start, Start.AddDays(2)) };
    private static readonly (DateOnly Start, DateOnly? End, bool Usable, DateOnly? Expiry)[] FullDrivers =
        { (Start, Start.AddDays(2), true, Start.AddDays(2)) };

    [Fact]
    public void Date_window_is_inclusive_and_bounded()
    {
        Assert.True(TransportService.IsSupportedDriverCoverageWindow(Start, Start.AddDays(29), Start));
        Assert.False(TransportService.IsSupportedDriverCoverageWindow(Start, Start.AddDays(30), Start));
        Assert.False(TransportService.IsSupportedDriverCoverageWindow(Start.AddDays(-1), Start, Start));
    }

    [Fact]
    public void Continuous_coverage_has_zero_risk_days()
    {
        var result = Project(FullRiders, FullRoutes, FullDrivers);
        Assert.Equal(0, result.AttentionDays);
        Assert.Null(result.FirstAttentionDate);
        Assert.Equal(1, result.PeakReservedRiders);
    }

    [Fact]
    public void One_day_driver_gap_is_detected_even_when_other_days_are_ready()
    {
        var drivers = new (DateOnly Start, DateOnly? End, bool Usable, DateOnly? Expiry)[]
        {
            (Start, Start, true, Start.AddDays(2)),
            (Start.AddDays(2), Start.AddDays(2), true, Start.AddDays(2))
        };
        var result = Project(FullRiders, FullRoutes, drivers);
        Assert.Equal(1, result.AttentionDays);
        Assert.Equal(Start.AddDays(1), result.FirstAttentionDate);
        Assert.Equal("NoDriver", result.FirstIssue);
    }

    [Fact]
    public void License_expiry_is_valid_on_expiry_day_only()
    {
        var drivers = new (DateOnly Start, DateOnly? End, bool Usable, DateOnly? Expiry)[]
        { (Start, Start.AddDays(2), true, Start) };
        var result = Project(FullRiders, FullRoutes, drivers);
        Assert.Equal(2, result.AttentionDays);
        Assert.Equal(Start.AddDays(1), result.FirstAttentionDate);
        Assert.Equal("Expired", result.FirstIssue);
    }

    [Fact]
    public void Overlapping_driver_links_are_reported_as_conflict()
    {
        var drivers = FullDrivers.Concat(FullDrivers).ToArray();
        var result = Project(FullRiders, FullRoutes, drivers);
        Assert.Equal(3, result.AttentionDays);
        Assert.Equal("DriverConflict", result.FirstIssue);
    }

    private static EduOS.Core.DTOs.Transport.TransportDriverCoverageWindowRowDto Project(
        IReadOnlyList<(DateOnly Start, DateOnly? End)> riders,
        IReadOnlyList<(DateOnly Start, DateOnly? End)> routes,
        IReadOnlyList<(DateOnly Start, DateOnly? End, bool Usable, DateOnly? Expiry)> drivers) =>
        TransportService.ProjectDriverCoverageWindow(Guid.NewGuid(), 1, "Campus", "Bus-1",
            true, 20, Start, Start.AddDays(2), riders, routes, drivers);
}
