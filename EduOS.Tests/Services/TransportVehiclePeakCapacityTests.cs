using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportVehiclePeakCapacityTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Fact]
    public void Disjoint_future_reservations_allow_reducing_capacity_to_one()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Today, Today.AddDays(4)),
            (Today.AddDays(5), Today.AddDays(10))
        };
        Assert.Equal(1, TransportService.PeakReservedSeats(bookings, Today, DateOnly.MaxValue).Peak);
    }

    [Fact]
    public void Inclusive_same_day_overlap_requires_two_seats()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Today, Today.AddDays(4)),
            (Today.AddDays(4), Today.AddDays(10))
        };
        Assert.Equal(2, TransportService.PeakReservedSeats(bookings, Today, DateOnly.MaxValue).Peak);
    }

    [Fact]
    public void Expired_reservations_do_not_block_future_capacity_reduction()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Today.AddDays(-10), Today.AddDays(-1)),
            (Today.AddDays(1), Today.AddDays(5))
        };
        Assert.Equal(1, TransportService.PeakReservedSeats(bookings, Today, DateOnly.MaxValue).Peak);
    }

    [Fact]
    public void Open_ended_overlaps_preserve_future_peak_capacity()
    {
        var bookings = new (DateOnly, DateOnly?)[]
        {
            (Today, null),
            (Today.AddDays(20), null)
        };
        Assert.Equal(2, TransportService.PeakReservedSeats(bookings, Today, DateOnly.MaxValue).Peak);
    }
}
