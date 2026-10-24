using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelFiniteReservationTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Fixed_stay_accepts_inclusive_ninety_day_limit()
    {
        Assert.True(HostelService.IsValidFiniteBookingPeriod(Today, Today.AddDays(89), Today));
        Assert.False(HostelService.IsValidFiniteBookingPeriod(Today, Today.AddDays(90), Today));
    }

    [Fact]
    public void Rejects_inverted_past_and_far_future_fixed_stays()
    {
        Assert.False(HostelService.IsValidFiniteBookingPeriod(Today, Today.AddDays(-1), Today));
        Assert.False(HostelService.IsValidFiniteBookingPeriod(Today.AddDays(-1), Today, Today));
        Assert.False(HostelService.IsValidFiniteBookingPeriod(Today.AddDays(366), Today.AddDays(367), Today));
    }

    [Fact]
    public void Open_ended_stay_preserves_existing_contract()
    {
        Assert.True(HostelService.IsValidFiniteBookingPeriod(Today.AddDays(-5), null, Today));
    }

    [Fact]
    public void Inclusive_boundaries_and_disjoint_periods_are_distinguished()
    {
        Assert.True(HostelService.BookingPeriodsOverlap(Today, Today.AddDays(3), Today.AddDays(3), Today.AddDays(6)));
        Assert.False(HostelService.BookingPeriodsOverlap(Today, Today.AddDays(3), Today.AddDays(4), Today.AddDays(6)));
        Assert.True(HostelService.BookingPeriodsOverlap(Today, null, Today.AddDays(100), null));
    }

    [Fact]
    public void Peak_room_capacity_uses_simultaneous_not_total_reservations()
    {
        var existing = new (DateOnly Start, DateOnly? End)[]
        {
            (Today, Today.AddDays(3)),
            (Today.AddDays(4), Today.AddDays(8))
        };
        Assert.Equal(2, HostelService.PeakConcurrentStays(Today.AddDays(2), Today.AddDays(6), existing));
    }
}
