using EduOS.BackgroundJobs.Jobs;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using Hangfire;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionAutoRenewJobTests
{
    [Theory]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.Trial, true)]
    [InlineData(SubscriptionState.Grace, true)]
    [InlineData(SubscriptionState.PendingPayment, false)]
    [InlineData(SubscriptionState.Cancelled, false)]
    [InlineData(SubscriptionState.Expired, false)]
    public void Only_opted_in_active_periods_inside_30_day_window_are_eligible(
        SubscriptionState state, bool expected)
    {
        var now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var row = new TenantSubscription
        {
            TenantId = 101, State = state, AutoRenew = true, BillingCycleCode = "Monthly",
            StartsAt = now.AddDays(-10), EndsAt = now.AddDays(5)
        };
        Assert.Equal(expected, SubscriptionAutoRenewJob.ShouldSchedule(row, now));
        row.AutoRenew = false;
        Assert.False(SubscriptionAutoRenewJob.ShouldSchedule(row, now));
        row.AutoRenew = true;
        row.EndsAt = now.AddDays(31);
        Assert.False(SubscriptionAutoRenewJob.ShouldSchedule(row, now));
        row.EndsAt = now;
        Assert.False(SubscriptionAutoRenewJob.ShouldSchedule(row, now));
        row.EndsAt = now.AddDays(5);
        row.BillingCycleCode = "Lifetime";
        Assert.False(SubscriptionAutoRenewJob.ShouldSchedule(row, now));
    }

    [Fact]
    public void Scheduler_has_retry_and_concurrency_protection()
    {
        var method = typeof(SubscriptionAutoRenewJob).GetMethod(nameof(SubscriptionAutoRenewJob.RunAsync));
        Assert.NotNull(method);
        Assert.NotNull(Attribute.GetCustomAttribute(method!, typeof(AutomaticRetryAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(method!, typeof(DisableConcurrentExecutionAttribute)));
    }
}
