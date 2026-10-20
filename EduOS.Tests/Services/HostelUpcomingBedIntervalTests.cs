using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelUpcomingBedIntervalTests
{
    private static readonly DateOnly Arrival = new(2026, 11, 10);

    [Fact]
    public void Disjoint_bookings_do_not_block_a_bed()
    {
        Assert.False(HostelService.ReservationIntervalsOverlap(
            Arrival.AddDays(-10), Arrival.AddDays(-1), Arrival, null));
        Assert.False(HostelService.ReservationIntervalsOverlap(
            Arrival, Arrival.AddDays(2), Arrival.AddDays(3), null));
    }

    [Fact]
    public void Inclusive_checkout_date_conflicts_with_same_day_arrival()
    {
        Assert.True(HostelService.ReservationIntervalsOverlap(
            Arrival.AddDays(-3), Arrival, Arrival, Arrival.AddDays(5)));
    }

    [Fact]
    public void Open_ended_bookings_conflict_with_later_reservations()
    {
        Assert.True(HostelService.ReservationIntervalsOverlap(
            Arrival.AddDays(-2), null, Arrival, Arrival.AddDays(5)));
    }

    [Fact]
    public void Two_finite_overlapping_stays_conflict()
    {
        Assert.True(HostelService.ReservationIntervalsOverlap(
            Arrival, Arrival.AddDays(5), Arrival.AddDays(3), Arrival.AddDays(10)));
    }
}
