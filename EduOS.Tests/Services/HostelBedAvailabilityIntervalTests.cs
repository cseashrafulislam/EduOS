using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelBedAvailabilityIntervalTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);

    [Fact]
    public void Disjoint_bookings_on_different_beds_do_not_exhaust_room_capacity()
    {
        var bookings = new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
        {
            (1, Start, Start.AddDays(2)),
            (2, Start.AddDays(3), Start.AddDays(5))
        };
        Assert.Equal(1, HostelService.PeakOccupiedBeds(bookings, Start, Start.AddDays(5)));
    }

    [Fact]
    public void Concurrent_bookings_on_different_beds_use_distinct_capacity_slots()
    {
        var bookings = new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
        {
            (1, Start, Start.AddDays(3)),
            (2, Start.AddDays(3), Start.AddDays(7))
        };
        // End dates are inclusive, so both beds occupy a slot on day three.
        Assert.Equal(2, HostelService.PeakOccupiedBeds(bookings, Start, Start.AddDays(7)));
    }

    [Fact]
    public void Duplicate_overlapping_allocations_on_one_bed_count_once()
    {
        var bookings = new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
        {
            (1, Start, Start.AddDays(5)),
            (1, Start.AddDays(2), null)
        };
        Assert.Equal(1, HostelService.PeakOccupiedBeds(bookings, Start, Start.AddDays(6)));
    }

    [Fact]
    public void Outside_interval_history_does_not_reduce_future_capacity()
    {
        var bookings = new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
        {
            (1, Start.AddDays(-20), Start.AddDays(-1)),
            (2, Start.AddDays(10), null)
        };
        Assert.Equal(0, HostelService.PeakOccupiedBeds(bookings, Start, Start.AddDays(9)));
    }

    [Fact]
    public void Invalid_interval_has_no_occupancy()
    {
        var bookings = new (long BedId, DateOnly StartDate, DateOnly? EndDate)[]
        {
            (1, Start, null)
        };
        Assert.Equal(0, HostelService.PeakOccupiedBeds(bookings, Start.AddDays(1), Start));
    }
}
