using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelPeakOccupancyForecastTests
{
    [Fact]
    public void Interval_is_inclusive_and_bounded_to_ninety_days()
    {
        var today = new DateOnly(2026, 10, 27);
        Assert.True(HostelService.IsSupportedPeakOccupancyPeriod(today, today, today));
        Assert.True(HostelService.IsSupportedPeakOccupancyPeriod(today, today.AddDays(89), today));
        Assert.True(HostelService.IsSupportedPeakOccupancyPeriod(today.AddDays(365), today.AddDays(365), today));
        Assert.False(HostelService.IsSupportedPeakOccupancyPeriod(today, today.AddDays(90), today));
        Assert.False(HostelService.IsSupportedPeakOccupancyPeriod(today.AddDays(-1), today, today));
        Assert.False(HostelService.IsSupportedPeakOccupancyPeriod(today, today.AddDays(-1), today));
    }

    [Fact]
    public void Disjoint_bed_bookings_do_not_inflate_peak()
    {
        var from = new DateOnly(2026, 11, 1);
        var row = HostelService.ProjectPeakOccupancyForecastRoom(1, "North", "A", "101", 2, true, true,
            new (long BedId, bool IsActive)[] { (1, true), (2, true) },
            new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
            {
                (1, from, from.AddDays(2)),
                (2, from.AddDays(3), from.AddDays(5))
            }, from, from.AddDays(5));
        Assert.Equal(1, row.PeakReservedBeds);
        Assert.Equal(from, row.PeakDate);
        Assert.Equal(1, row.MinimumAvailableBeds);
        Assert.Equal(0, row.AttentionDays);
    }

    [Fact]
    public void Overlapping_bookings_peak_on_first_overlap_day()
    {
        var from = new DateOnly(2026, 11, 1);
        var row = HostelService.ProjectPeakOccupancyForecastRoom(2, "North", "A", "102", 2, true, true,
            new (long BedId, bool IsActive)[] { (1, true), (2, true) },
            new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
            {
                (1, from, from.AddDays(3)),
                (2, from.AddDays(2), from.AddDays(4))
            }, from, from.AddDays(4));
        Assert.Equal(2, row.PeakReservedBeds);
        Assert.Equal(from.AddDays(2), row.PeakDate);
        Assert.Equal(0, row.MinimumAvailableBeds);
        Assert.Equal(2, row.AttentionDays);
        Assert.Equal("Full at peak", row.Status);
    }

    [Fact]
    public void Duplicate_reservations_count_one_bed_but_flag_risk_days()
    {
        var from = new DateOnly(2026, 11, 1);
        var row = HostelService.ProjectPeakOccupancyForecastRoom(3, "North", "A", "103", 2, true, true,
            new (long BedId, bool IsActive)[] { (1, true), (2, true) },
            new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
            {
                (1, from, from.AddDays(1)),
                (1, from, from.AddDays(1))
            }, from, from.AddDays(3));
        Assert.Equal(1, row.PeakReservedBeds);
        Assert.Equal(1, row.MaximumDuplicateBookings);
        Assert.Equal(2, row.AttentionDays);
        Assert.Equal("Duplicate bookings", row.Status);
    }

    [Fact]
    public void Inactive_bed_and_inactive_room_are_not_reported_available()
    {
        var day = new DateOnly(2026, 11, 1);
        var row = HostelService.ProjectPeakOccupancyForecastRoom(4, "North", "A", "104", 2, true, false,
            new (long BedId, bool IsActive)[] { (1, false), (2, true) },
            new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
            {
                (1, day, null)
            }, day, day);
        Assert.Equal(0, row.MinimumAvailableBeds);
        Assert.Equal(1, row.MaximumInactiveReservedBeds);
        Assert.Equal(1, row.AttentionDays);
        Assert.True(row.NeedsAttention);
    }
}
