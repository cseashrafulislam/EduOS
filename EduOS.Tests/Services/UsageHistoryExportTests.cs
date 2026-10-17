using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class UsageHistoryExportTests
{
    [Fact]
    public async Task Unauthenticated_user_cannot_export()
    {
        var current = new Mock<ICurrentUserService>();
        var service = new UsageHistoryService(null!, current.Object, NullLogger<UsageHistoryService>.Instance);
        Assert.Equal(403, (await service.ExportHistoryCsvAsync(null, null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_date_range_is_rejected_before_query()
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.IsTenantAdmin).Returns(true);
        current.SetupGet(x => x.TenantId).Returns(5);
        var service = new UsageHistoryService(null!, current.Object, NullLogger<UsageHistoryService>.Instance);
        Assert.Equal(400, (await service.ExportHistoryCsvAsync(
            new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 16))).StatusCode);
        Assert.Equal(400, (await service.ExportHistoryCsvAsync(
            new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 17))).StatusCode);
    }

    [Fact]
    public void Export_endpoint_requires_admin_and_is_a_get()
    {
        var type = typeof(UsageHistoryApiController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true));
        var action = type.GetMethod("Export");
        Assert.NotNull(action);
        Assert.NotEmpty(action!.GetCustomAttributes(typeof(HttpGetAttribute), true));
    }
}
