using EduOS.App.Controllers.Api;
using EduOS.BackgroundJobs.Jobs;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionExpiryJobTests
{
    [Theory]
    [InlineData(SubscriptionState.Trial, true)]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.Grace, true)]
    [InlineData(SubscriptionState.PendingPayment, false)]
    [InlineData(SubscriptionState.Suspended, false)]
    [InlineData(SubscriptionState.Cancelled, false)]
    [InlineData(SubscriptionState.Expired, false)]
    public void Only_elapsed_active_periods_expire(SubscriptionState state, bool eligible)
    {
        var now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var row = new TenantSubscription { TenantId = 7, State = state, EndsAt = now.AddMinutes(-1) };
        Assert.Equal(eligible, SubscriptionExpiryJob.ShouldExpire(row, now));
        row.EndsAt = now.AddMinutes(1);
        Assert.False(SubscriptionExpiryJob.ShouldExpire(row, now));
        row.EndsAt = now.AddMinutes(-1);
        row.IsDeleted = true;
        Assert.False(SubscriptionExpiryJob.ShouldExpire(row, now));
    }

    [Fact]
    public void Job_is_concurrency_protected_and_retryable()
    {
        var run = typeof(SubscriptionExpiryJob).GetMethod(nameof(SubscriptionExpiryJob.RunAsync));
        Assert.NotNull(run);
        Assert.NotNull(Attribute.GetCustomAttribute(run!, typeof(DisableConcurrentExecutionAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(run!, typeof(AutomaticRetryAttribute)));
    }

    [Fact]
    public void Lifecycle_endpoint_is_tenant_admin_read_only()
    {
        var api = typeof(SubscriptionController);
        Assert.Equal("TenantAdmin", Assert.Single(api.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        var action = api.GetMethod(nameof(SubscriptionController.LifecycleStatus));
        Assert.NotNull(action);
        Assert.NotEmpty(action!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.Empty(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }
}
