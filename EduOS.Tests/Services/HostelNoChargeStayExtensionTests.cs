using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelNoChargeStayExtensionTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact] public void Active_finite_stay_can_extend() =>
        Assert.True(HostelService.CanExtendNoChargeStay(Today.AddDays(-10), Today.AddDays(10), Today.AddDays(20), Today));

    [Fact] public void Upcoming_stay_can_extend() =>
        Assert.True(HostelService.CanExtendNoChargeStay(Today.AddDays(5), Today.AddDays(10), Today.AddDays(15), Today));

    [Fact] public void Expired_and_open_ended_stays_cannot_extend()
    {
        Assert.False(HostelService.CanExtendNoChargeStay(Today.AddDays(-30), Today.AddDays(-1), Today.AddDays(1), Today));
        Assert.False(HostelService.CanExtendNoChargeStay(Today, null, Today.AddDays(1), Today));
    }

    [Fact] public void Equal_or_earlier_checkout_is_not_extension()
    {
        Assert.False(HostelService.CanExtendNoChargeStay(Today, Today.AddDays(5), Today.AddDays(5), Today));
        Assert.False(HostelService.CanExtendNoChargeStay(Today, Today.AddDays(5), Today.AddDays(4), Today));
    }

    [Fact] public void Original_ninety_day_limit_is_preserved()
    {
        Assert.True(HostelService.CanExtendNoChargeStay(Today, Today.AddDays(10), Today.AddDays(89), Today));
        Assert.False(HostelService.CanExtendNoChargeStay(Today, Today.AddDays(10), Today.AddDays(90), Today));
    }

    [Fact] public void Inclusive_checkout_overlap_is_detected()
    {
        Assert.True(HostelService.BookingPeriodsOverlap(Today.AddDays(6), Today.AddDays(10),
            Today.AddDays(10), Today.AddDays(12)));
        Assert.False(HostelService.BookingPeriodsOverlap(Today.AddDays(6), Today.AddDays(10),
            Today.AddDays(11), Today.AddDays(12)));
    }
}
