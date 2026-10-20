using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportUpcomingVehicleCapacityTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);

    [Fact]
    public void Disjoint_bookings_leave_a_seat_available_for_entire_requested_period()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Start, Start.AddDays(2)),
            (Start.AddDays(3), Start.AddDays(6))
        };
        var peak = TransportService.PeakReservedSeats(bookings, Start, Start.AddDays(6)).Peak;
        Assert.Equal(1, peak);
        Assert.True(peak < 2);
    }

    [Fact]
    public void Same_day_changeover_is_a_capacity_conflict_with_inclusive_end_dates()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Start, Start.AddDays(2)),
            (Start.AddDays(2), Start.AddDays(6))
        };
        var peak = TransportService.PeakReservedSeats(bookings, Start, Start.AddDays(6)).Peak;
        Assert.Equal(2, peak);
        Assert.False(peak < 2);
    }

    [Fact]
    public void Indefinite_requested_stay_counts_later_reservations()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Start, Start.AddDays(10)),
            (Start.AddDays(11), null)
        };
        var peak = TransportService.PeakReservedSeats(bookings, Start, DateOnly.MaxValue).Peak;
        Assert.Equal(1, peak);
    }

    [Fact]
    public void Indefinite_requested_stay_detects_future_concurrent_bookings()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Start, null),
            (Start.AddDays(30), null)
        };
        var peak = TransportService.PeakReservedSeats(bookings, Start, DateOnly.MaxValue).Peak;
        Assert.Equal(2, peak);
    }
}
