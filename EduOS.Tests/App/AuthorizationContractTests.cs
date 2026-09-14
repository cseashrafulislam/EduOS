using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.App.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

public class AuthorizationContractTests
{
    [Fact]
    public void Authorization_fallback_requires_authenticated_users()
    {
        var services = new ServiceCollection();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Production);

        services.AddLogging();
        services.AddIdentityConfiguration(environment.Object);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        options.FallbackPolicy.Should().NotBeNull();
        options.FallbackPolicy!.Requirements
            .OfType<DenyAnonymousAuthorizationRequirement>()
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Public_account_and_catalog_routes_are_explicitly_anonymous()
    {
        var publicAccountActions = new[]
        {
            nameof(AccountController.Login),
            nameof(AccountController.Signup),
            nameof(AccountController.SignupSuccess),
            nameof(AccountController.VerifyEmail),
            nameof(AccountController.VerifyEmailSuccess),
            nameof(AccountController.VerifyFailed),
            nameof(AccountController.ForgotPassword),
            nameof(AccountController.ResetPassword),
            nameof(AccountController.MfaChallenge)
        };

        foreach (var actionName in publicAccountActions)
        {
            typeof(AccountController).GetMethod(actionName)!
                .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
                .Should().NotBeEmpty($"{actionName} is intentionally public");
        }

        typeof(PricingController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
            .Should().NotBeEmpty();
        typeof(PlatformCatalogController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Account_profile_is_not_public()
    {
        var profile = typeof(AccountController).GetMethod(nameof(AccountController.Profile))!;

        profile.GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        profile.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().BeEmpty();
    }
}
