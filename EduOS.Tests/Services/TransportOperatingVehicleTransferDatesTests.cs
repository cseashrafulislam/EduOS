using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportOperatingVehicleTransferDatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    [Fact]
    public void Running_open_ended_reservation_can_transfer_from_today()
    {
        Assert.True(TransportService.CanTransferOperatingRiderDates(Today.AddDays(-10), null, Today, Today));
    }

    [Fact]
    public void Finite_reservation_can_transfer_at_last_day()
    {
        Assert.True(TransportService.CanTransferOperatingRiderDates(Today.AddDays(-3), Today.AddDays(1), Today.AddDays(1), Today));
    }

    [Theory]
    [InlineData(-10, -1, 0)]
    [InlineData(-10, 2, -1)]
    [InlineData(0, 10, 0)]
    [InlineData(2, 10, 3)]
    [InlineData(-10, 2, 3)]
    public void Invalid_transfer_dates_are_rejected(int startOffset, int endOffset, int effectiveOffset)
    {
        Assert.False(TransportService.CanTransferOperatingRiderDates(
            Today.AddDays(startOffset), Today.AddDays(endOffset), Today.AddDays(effectiveOffset), Today));
    }
}
