using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelOccupancyForecastTests
{
    [Fact]
    public void Forecast_horizon_is_bounded_and_inclusive()
    {
        var today = new DateOnly(2026, 10, 26);
        Assert.True(HostelService.IsSupportedOccupancyForecastDate(today, today));
        Assert.True(HostelService.IsSupportedOccupancyForecastDate(today.AddDays(365), today));
        Assert.False(HostelService.IsSupportedOccupancyForecastDate(today.AddDays(-1), today));
        Assert.False(HostelService.IsSupportedOccupancyForecastDate(today.AddDays(366), today));
        Assert.True(HostelService.IsSupportedOccupancyForecastDate(
            new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 28)));
    }

    [Fact]
    public void Duplicate_bookings_count_one_physical_bed_and_raise_risk()
    {
        var row = HostelService.ProjectOccupancyForecastRoom(7, "North", "A", "101", 2, true, true,
            new (long BedId, bool IsActive)[] { (1, true), (2, true) },
            new long[] { 1, 1 });
        Assert.Equal(1, row.ReservedBeds);
        Assert.Equal(1, row.AvailableBeds);
        Assert.Equal(1, row.DuplicateReservations);
        Assert.Equal("Duplicate bookings", row.Status);
        Assert.True(row.NeedsAttention);
    }

    [Fact]
    public void Inactive_bed_reservation_consumes_capacity_and_is_flagged()
    {
        var row = HostelService.ProjectOccupancyForecastRoom(8, "North", "A", "102", 2, true, true,
            new (long BedId, bool IsActive)[] { (1, true), (2, false), (3, true) },
            new long[] { 2 });
        Assert.Equal(1, row.InactiveReservedBeds);
        Assert.Equal(1, row.AvailableBeds);
        Assert.Equal("Inactive bed reserved", row.Status);
        Assert.True(row.NeedsAttention);
    }

    [Fact]
    public void Full_or_overcapacity_room_is_not_offered_as_available()
    {
        var full = HostelService.ProjectOccupancyForecastRoom(9, "North", "A", "103", 1, true, true,
            new (long BedId, bool IsActive)[] { (1, true) }, new long[] { 1 });
        Assert.Equal(0, full.AvailableBeds);
        Assert.Equal("Full", full.Status);
        Assert.True(full.NeedsAttention);
        var over = HostelService.ProjectOccupancyForecastRoom(10, "North", "A", "104", 1, true, true,
            new (long BedId, bool IsActive)[] { (1, true), (2, true) }, new long[] { 1, 2 });
        Assert.Equal(0, over.AvailableBeds);
        Assert.Equal("Over capacity", over.Status);
        Assert.True(over.NeedsAttention);
    }

    [Fact]
    public void Inactive_room_with_reservation_remains_visible_as_risk()
    {
        var row = HostelService.ProjectOccupancyForecastRoom(11, "North", "A", "105", 2, true, false,
            new (long BedId, bool IsActive)[] { (1, true) }, new long[] { 1 });
        Assert.Equal(0, row.AvailableBeds);
        Assert.Equal("Inactive with reservations", row.Status);
        Assert.True(row.NeedsAttention);
    }
}
