using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelUpcomingStartRescheduleTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Future_reservation_can_move_earlier_or_later()
    {
        Assert.True(HostelService.CanRescheduleUpcomingStay(Today.AddDays(20), null, Today.AddDays(2), Today));
        Assert.True(HostelService.CanRescheduleUpcomingStay(Today.AddDays(20), Today.AddDays(30), Today.AddDays(30), Today));
    }

    [Fact]
    public void Started_or_out_of_range_reservation_cannot_move()
    {
        Assert.False(HostelService.CanRescheduleUpcomingStay(Today, null, Today.AddDays(2), Today));
        Assert.False(HostelService.CanRescheduleUpcomingStay(Today.AddDays(20), null, Today, Today));
        Assert.False(HostelService.CanRescheduleUpcomingStay(Today.AddDays(20), Today.AddDays(30), Today.AddDays(31), Today));
        Assert.False(HostelService.CanRescheduleUpcomingStay(Today.AddDays(20), null, Today.AddYears(3), Today));
    }

    [Fact]
    public void Disjoint_reservations_do_not_overcount_capacity()
    {
        var existing = new (DateOnly Start, DateOnly? End)[]
        {
            (Today.AddDays(2), Today.AddDays(3)),
            (Today.AddDays(8), Today.AddDays(9))
        };
        Assert.Equal(2, HostelService.PeakConcurrentStays(Today.AddDays(2), Today.AddDays(9), existing));
    }

    [Fact]
    public void Inclusive_overlap_and_open_ended_stays_are_counted()
    {
        var existing = new (DateOnly Start, DateOnly? End)[]
        {
            (Today.AddDays(4), Today.AddDays(7)),
            (Today.AddDays(7), null)
        };
        Assert.Equal(3, HostelService.PeakConcurrentStays(Today.AddDays(5), null, existing));
    }

    [Fact]
    public void End_boundary_release_is_not_an_overlap()
    {
        var existing = new (DateOnly Start, DateOnly? End)[]
        {
            (Today.AddDays(2), Today.AddDays(3))
        };
        Assert.Equal(1, HostelService.PeakConcurrentStays(Today.AddDays(4), null, existing));
    }
}
