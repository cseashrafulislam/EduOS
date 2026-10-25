using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelUpcomingNoChargeShorteningTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    [Fact] public void Upcoming_fixed_stay_can_release_unused_days() =>
        Assert.True(HostelService.CanShortenUpcomingNoChargeStay(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(10), Today));

    [Fact] public void Arrival_day_is_valid_inclusive_checkout() =>
        Assert.True(HostelService.CanShortenUpcomingNoChargeStay(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(5), Today));

    [Fact] public void Cannot_shorten_stay_that_has_started() =>
        Assert.False(HostelService.CanShortenUpcomingNoChargeStay(
            Today, Today.AddDays(20), Today.AddDays(10), Today));

    [Fact] public void Cannot_shorten_expired_or_invalid_period() =>
        Assert.False(HostelService.CanShortenUpcomingNoChargeStay(
            Today.AddDays(10), Today.AddDays(5), Today.AddDays(10), Today));

    [Fact] public void Open_ended_reservation_is_not_eligible() =>
        Assert.False(HostelService.CanShortenUpcomingNoChargeStay(
            Today.AddDays(5), null, Today.AddDays(10), Today));

    [Fact] public void Same_or_later_checkout_is_not_a_shortening()
    {
        Assert.False(HostelService.CanShortenUpcomingNoChargeStay(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(20), Today));
        Assert.False(HostelService.CanShortenUpcomingNoChargeStay(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(21), Today));
    }

    [Fact] public void Checkout_cannot_precede_arrival() =>
        Assert.False(HostelService.CanShortenUpcomingNoChargeStay(
            Today.AddDays(5), Today.AddDays(20), Today.AddDays(4), Today));
}
