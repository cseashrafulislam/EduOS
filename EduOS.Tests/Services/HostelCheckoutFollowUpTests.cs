using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelCheckoutFollowUpTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    [Fact]
    public void Expired_active_reservation_is_overdue_not_automatically_closed() =>
        Assert.Equal("Overdue checkout", HostelService.ClassifyCheckoutFollowUp(
            Today.AddDays(-20), Today.AddDays(-1), Today));

    [Fact]
    public void Checkout_on_current_day_is_actionable() =>
        Assert.Equal("Checkout today", HostelService.ClassifyCheckoutFollowUp(
            Today.AddDays(-7), Today, Today));

    [Fact]
    public void Future_checkout_is_scheduled() =>
        Assert.Equal("Checkout upcoming", HostelService.ClassifyCheckoutFollowUp(
            Today.AddDays(-1), Today.AddDays(30), Today));

    [Fact]
    public void Invalid_date_order_is_flagged_first() =>
        Assert.Equal("Invalid dates", HostelService.ClassifyCheckoutFollowUp(
            Today, Today.AddDays(-1), Today));
}
