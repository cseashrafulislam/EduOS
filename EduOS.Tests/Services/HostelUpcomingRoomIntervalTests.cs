using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelUpcomingRoomIntervalTests
{
    private static readonly DateOnly Arrival = new(2026, 11, 10);

    [Fact]
    public void Disjoint_bed_reservations_do_not_consume_capacity_concurrently()
    {
        var bookings = new (long, DateOnly, DateOnly?)[]
        {
            (1, Arrival, Arrival.AddDays(3)),
            (2, Arrival.AddDays(4), Arrival.AddDays(8))
        };
        Assert.Equal(1, HostelService.PeakOccupiedBedsForReservationInterval(
            bookings, Arrival, Arrival.AddDays(8)));
    }

    [Fact]
    public void Overlapping_different_beds_consume_two_slots()
    {
        var bookings = new (long, DateOnly, DateOnly?)[]
        {
            (1, Arrival, Arrival.AddDays(3)),
            (2, Arrival.AddDays(3), Arrival.AddDays(8))
        };
        Assert.Equal(2, HostelService.PeakOccupiedBedsForReservationInterval(
            bookings, Arrival, Arrival.AddDays(8)));
    }

    [Fact]
    public void Duplicate_overlapping_allocations_on_same_bed_count_once()
    {
        var bookings = new (long, DateOnly, DateOnly?)[]
        {
            (1, Arrival, Arrival.AddDays(4)),
            (1, Arrival.AddDays(2), Arrival.AddDays(7)),
            (2, Arrival.AddDays(5), Arrival.AddDays(8))
        };
        Assert.Equal(2, HostelService.PeakOccupiedBedsForReservationInterval(
            bookings, Arrival, Arrival.AddDays(8)));
    }

    [Fact]
    public void Open_ended_reservations_count_without_daily_iteration()
    {
        var bookings = new (long, DateOnly, DateOnly?)[]
        {
            (1, Arrival, null),
            (2, Arrival.AddDays(10), null)
        };
        Assert.Equal(2, HostelService.PeakOccupiedBedsForReservationInterval(
            bookings, Arrival, null));
    }

    [Fact]
    public void Past_bookings_do_not_count_for_future_stay()
    {
        var bookings = new (long, DateOnly, DateOnly?)[]
        {
            (1, Arrival.AddDays(-10), Arrival.AddDays(-1))
        };
        Assert.Equal(0, HostelService.PeakOccupiedBedsForReservationInterval(
            bookings, Arrival, Arrival.AddDays(2)));
    }
}
