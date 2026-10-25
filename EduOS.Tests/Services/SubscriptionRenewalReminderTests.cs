using EduOS.App.Controllers.Api;
using EduOS.BackgroundJobs.Jobs;
using EduOS.Service.Services.SaaS;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionRenewalReminderTests
{
    [Theory]
    [InlineData(8, null)]
    [InlineData(7, "due-7")]
    [InlineData(4, "due-7")]
    [InlineData(3, "due-3")]
    [InlineData(1, "due-3")]
    [InlineData(0, "due")]
    [InlineData(-2, "due")]
    [InlineData(-3, "overdue")]
    [InlineData(-14, "overdue")]
    [InlineData(-15, null)]
    public void Automatic_reminders_have_bounded_nonoverlapping_stages(int days, string? stage) =>
        Assert.Equal(stage, SubscriptionRenewalReminderService.AutomaticStage(days));

    [Fact]
    public void Job_has_retry_and_concurrency_guards()
    {
        var run = typeof(SubscriptionRenewalReminderJob).GetMethod(nameof(SubscriptionRenewalReminderJob.RunAsync));
        Assert.NotNull(run);
        Assert.NotNull(Attribute.GetCustomAttribute(run!, typeof(AutomaticRetryAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(run!, typeof(DisableConcurrentExecutionAttribute)));
    }

    [Fact]
    public void Reminder_post_requires_tenant_admin_and_antiforgery()
    {
        var controller = typeof(SubscriptionController);
        Assert.Equal("TenantAdmin", Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        var method = controller.GetMethod(nameof(SubscriptionController.SendRenewalReminder));
        Assert.NotNull(method);
        Assert.NotEmpty(method!.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }
}
