using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class UsageQuotaOverviewTests
{
    [Fact]
    public async Task Quota_overview_requires_tenant_administrator()
    {
        var current = new Mock<ICurrentUserService>();
        var service = new UsageHistoryService(null!, current.Object, NullLogger<UsageHistoryService>.Instance);
        Assert.Equal(403, (await service.GetQuotaOverviewAsync()).StatusCode);
    }

    [Fact]
    public void Quota_overview_endpoint_is_tenant_admin_only_and_read_only()
    {
        var controller = typeof(UsageHistoryApiController);
        var authorization = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", authorization.Roles);
        var action = controller.GetMethod(nameof(UsageHistoryApiController.QuotaOverview));
        Assert.NotNull(action);
        Assert.NotEmpty(action!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.Empty(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }

    [Fact]
    public void Live_user_seat_metric_is_separate_from_saved_daily_metrics()
    {
        var overview = new UsageQuotaOverviewDto
        {
            CurrentUserSeats = UsageQuotaMetricDto.FromCount("Institution users (live)", 2, 2)
        };
        Assert.False(overview.HasSnapshot);
        Assert.Empty(overview.Metrics);
        Assert.Equal(2, overview.CurrentUserSeats!.Used);
        Assert.True(overview.CurrentUserSeats.IsNearLimit);
    }

    [Fact]
    public void Storage_quota_thresholds_keep_bytes_precise_and_distinguish_unlimited()
    {
        var below = UsageStorageQuotaMetricDto.FromBytes(943718, 1);
        Assert.Equal(1048576L, below.LimitBytes);
        Assert.False(below.IsNearLimit);
        var atLimit = UsageStorageQuotaMetricDto.FromBytes(1048576, 1);
        Assert.True(atLimit.IsNearLimit);
        Assert.False(atLimit.IsOverLimit);
        Assert.True(UsageStorageQuotaMetricDto.FromBytes(1048577, 1).IsOverLimit);
        Assert.True(UsageStorageQuotaMetricDto.FromBytes(20, 0).IsUnlimited);
        Assert.Null(UsageStorageQuotaMetricDto.FromBytes(20, null).LimitBytes);
    }

    [Theory]
    [InlineData(89, 100, false, false)]
    [InlineData(90, 100, true, false)]
    [InlineData(100, 100, true, false)]
    [InlineData(110, 100, true, true)]
    public void Quota_thresholds_use_the_persisted_plan_limit(int used, int limit, bool near, bool over)
    {
        var metric = UsageQuotaMetricDto.FromCount("Students", used, limit);
        Assert.Equal(near, metric.IsNearLimit);
        Assert.Equal(over, metric.IsOverLimit);
        Assert.Equal(limit, metric.Limit);
    }

    [Fact]
    public void Unlimited_and_missing_plan_are_not_confused()
    {
        var unlimited = UsageQuotaMetricDto.FromCount("Campuses", 5, 0);
        var missing = UsageQuotaMetricDto.FromCount("Campuses", 5, null);
        Assert.True(unlimited.IsUnlimited);
        Assert.False(missing.IsUnlimited);
        Assert.Null(unlimited.UtilizationPercent);
        Assert.Null(missing.UtilizationPercent);
        Assert.Null(missing.Limit);
    }
}
