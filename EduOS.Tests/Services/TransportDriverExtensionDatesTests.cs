using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportDriverExtensionDatesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    [Fact]
    public void Renewed_license_permits_operating_assignment_extension()
    {
        Assert.True(TransportService.CanExtendOperatingDriverDates(
            Today.AddDays(-30), Today.AddDays(2), Today.AddDays(40),
            Today.AddDays(60), Today));
    }

    [Theory]
    [InlineData(-1, 20, 30)]
    [InlineData(2, 2, 30)]
    [InlineData(2, 31, 30)]
    public void Invalid_dates_cannot_extend(int currentEndOffset, int requestedOffset, int licenseOffset)
    {
        Assert.False(TransportService.CanExtendOperatingDriverDates(
            Today.AddDays(-10), Today.AddDays(currentEndOffset),
            Today.AddDays(requestedOffset), Today.AddDays(licenseOffset), Today));
    }

    [Fact]
    public void Scheduled_or_open_ended_assignments_cannot_use_operating_extension()
    {
        Assert.False(TransportService.CanExtendOperatingDriverDates(
            Today.AddDays(1), Today.AddDays(5), Today.AddDays(10), Today.AddDays(30), Today));
        Assert.False(TransportService.CanExtendOperatingDriverDates(
            Today.AddDays(-1), null, Today.AddDays(10), Today.AddDays(30), Today));
    }
}
