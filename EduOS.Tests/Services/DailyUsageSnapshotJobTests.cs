using EduOS.App.Controllers.Api;
using EduOS.BackgroundJobs.Jobs;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using EduOS.Core.Interfaces;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DailyUsageSnapshotJobTests
{
    [Fact]
    public void Job_is_concurrency_protected_and_retryable()
    {
        var run = typeof(DailyUsageSnapshotJob).GetMethod(nameof(DailyUsageSnapshotJob.RunAsync));
        Assert.NotNull(run);
        Assert.NotNull(Attribute.GetCustomAttribute(run!, typeof(DisableConcurrentExecutionAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(run!, typeof(AutomaticRetryAttribute)));
    }

    [Fact]
    public async Task Capture_and_automation_status_require_tenant_admin()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new UsageHistoryService(null!, user.Object, NullLogger<UsageHistoryService>.Instance);
        Assert.Equal(403, (await service.CaptureTodayAsync()).StatusCode);
        Assert.Equal(403, (await service.GetAutomationStatusAsync()).StatusCode);
    }

    [Fact]
    public void Automation_status_is_tenant_admin_read_only()
    {
        var api = typeof(UsageHistoryApiController);
        Assert.Equal("TenantAdmin", Assert.Single(api.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        var action = api.GetMethod(nameof(UsageHistoryApiController.AutomationStatus));
        Assert.NotNull(action);
        Assert.NotEmpty(action!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.Empty(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }
}
