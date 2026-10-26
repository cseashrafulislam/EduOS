using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportStopDemandForecastDateTests
{
    [Fact]
    public void Includes_today_and_last_supported_future_day()
    {
        var today = new DateOnly(2026, 10, 26);
        Assert.True(TransportService.IsSupportedStopDemandDate(today, today));
        Assert.True(TransportService.IsSupportedStopDemandDate(today.AddDays(365), today));
    }

    [Fact]
    public void Rejects_past_and_out_of_range_dates()
    {
        var today = new DateOnly(2026, 10, 26);
        Assert.False(TransportService.IsSupportedStopDemandDate(today.AddDays(-1), today));
        Assert.False(TransportService.IsSupportedStopDemandDate(today.AddDays(366), today));
    }

    [Fact]
    public void Leap_day_is_valid_within_forecast_horizon()
    {
        var today = new DateOnly(2024, 2, 28);
        Assert.True(TransportService.IsSupportedStopDemandDate(new DateOnly(2024, 2, 29), today));
    }
}
