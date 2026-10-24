using EduOS.Core.DTOs.Transport;
using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportRiderReservationPeriodTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);

    [Fact]
    public void Disjoint_existing_bookings_use_one_seat_at_peak()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start, Start.AddDays(2)),
            (Start.AddDays(3), Start.AddDays(6))
        };
        Assert.Equal(1, TransportService.PeakReservedSeats(reservations, Start, Start.AddDays(6)).Peak);
    }

    [Fact]
    public void Inclusive_same_day_handover_uses_two_seats()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start, Start.AddDays(2)),
            (Start.AddDays(2), Start.AddDays(6))
        };
        Assert.Equal(2, TransportService.PeakReservedSeats(reservations, Start, Start.AddDays(6)).Peak);
    }

    [Fact]
    public void Open_ended_request_considers_future_reservations()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start.AddDays(10), null),
            (Start.AddDays(15), null)
        };
        Assert.Equal(2, TransportService.PeakReservedSeats(reservations, Start, DateOnly.MaxValue).Peak);
    }

    [Fact]
    public void Finite_request_ignores_reservations_after_its_end()
    {
        var reservations = new (DateOnly, DateOnly?)[]
        {
            (Start.AddDays(7), null),
            (Start.AddDays(8), null)
        };
        Assert.Equal(0, TransportService.PeakReservedSeats(reservations, Start, Start.AddDays(6)).Peak);
    }

    [Fact]
    public void Reservation_request_exposes_optional_end_date()
    {
        var request = new AssignStudentTransportRequestDto { StartDate = Start, EndDate = Start.AddDays(30) };
        Assert.Equal(Start.AddDays(30), request.EndDate);
        request.EndDate = null;
        Assert.Null(request.EndDate);
    }
}
