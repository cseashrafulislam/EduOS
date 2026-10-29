using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelResidentRosterTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);

    [Fact]
    public void Historical_and_today_rosters_are_supported()
    {
        Assert.True(HostelService.IsSupportedResidentRosterDate(Today, Today));
        Assert.True(HostelService.IsSupportedResidentRosterDate(Today.AddYears(-5), Today));
    }

    [Fact]
    public void Future_and_invalid_historical_dates_are_rejected()
    {
        Assert.False(HostelService.IsSupportedResidentRosterDate(Today.AddDays(1), Today));
        Assert.False(HostelService.IsSupportedResidentRosterDate(new DateOnly(1999, 12, 31), Today));
    }
}
