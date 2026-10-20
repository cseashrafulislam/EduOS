using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelAllocationClosureTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public void Same_day_checkout_is_valid() =>
        Assert.True(HostelService.IsValidClosureDate(Today, Today, Today));

    [Fact]
    public void Historical_checkout_within_stay_is_valid() =>
        Assert.True(HostelService.IsValidClosureDate(Today.AddDays(-10), Today.AddDays(-2), Today));

    [Fact]
    public void Future_checkout_cannot_release_occupied_bed_early() =>
        Assert.False(HostelService.IsValidClosureDate(Today.AddDays(-3), Today.AddDays(1), Today));

    [Fact]
    public void Future_reservation_must_use_cancellation_not_checkout() =>
        Assert.False(HostelService.IsValidClosureDate(Today.AddDays(2), Today.AddDays(2), Today));

    [Fact]
    public void Checkout_before_start_is_rejected() =>
        Assert.False(HostelService.IsValidClosureDate(Today.AddDays(-3), Today.AddDays(-4), Today));
}
