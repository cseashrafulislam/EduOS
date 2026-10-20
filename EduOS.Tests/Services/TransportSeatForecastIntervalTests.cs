using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportSeatForecastIntervalTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);

    [Fact]
    public void Disjoint_reservations_do_not_exhaust_capacity_concurrently()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start, Start.AddDays(2)),
            (Start.AddDays(3), Start.AddDays(6))
        };
        var result = TransportService.PeakReservedSeats(reservations, Start, Start.AddDays(6));
        Assert.Equal(1, result.Peak);
        Assert.Equal(Start, result.PeakDate);
    }

    [Fact]
    public void Inclusive_end_date_counts_both_riders_on_changeover_day()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start, Start.AddDays(2)),
            (Start.AddDays(2), Start.AddDays(6))
        };
        var result = TransportService.PeakReservedSeats(reservations, Start, Start.AddDays(6));
        Assert.Equal(2, result.Peak);
        Assert.Equal(Start.AddDays(2), result.PeakDate);
    }

    [Fact]
    public void Open_ended_reservation_counts_in_future_window()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start.AddDays(-30), null),
            (Start.AddDays(5), null)
        };
        Assert.Equal(2, TransportService.PeakReservedSeats(reservations, Start, Start.AddDays(10)).Peak);
    }

    [Fact]
    public void Expired_or_invalid_reservations_do_not_consume_seats()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start.AddDays(-5), Start.AddDays(-1)),
            (Start.AddDays(11), null),
            (Start.AddDays(6), Start.AddDays(5))
        };
        Assert.Equal(0, TransportService.PeakReservedSeats(reservations, Start, Start.AddDays(10)).Peak);
    }

    [Fact]
    public void No_reservations_returns_first_requested_day()
    {
        var result = TransportService.PeakReservedSeats(
            Array.Empty<(DateOnly, DateOnly?)>(), Start, Start.AddDays(10));
        Assert.Equal(0, result.Peak);
        Assert.Equal(Start, result.PeakDate);
    }
}
