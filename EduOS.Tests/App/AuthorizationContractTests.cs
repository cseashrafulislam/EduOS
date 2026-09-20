using EduOS.App.Authorization;
using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.App.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
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
        options.FallbackPolicy!.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().NotBeEmpty();
    }

    [Fact]
    public void Public_account_catalog_and_error_routes_are_explicitly_anonymous()
    {
        var publicAccountActions = new[]
        {
            nameof(AccountController.Login), nameof(AccountController.Signup), nameof(AccountController.SignupSuccess),
            nameof(AccountController.VerifyEmail), nameof(AccountController.VerifyEmailSuccess), nameof(AccountController.VerifyFailed),
            nameof(AccountController.ForgotPassword), nameof(AccountController.ResetPassword), nameof(AccountController.MfaChallenge)
        };
        foreach (var actionName in publicAccountActions)
            typeof(AccountController).GetMethod(actionName)!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().NotBeEmpty($"{actionName} is intentionally public");
        typeof(PricingController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().NotBeEmpty();
        typeof(PlatformCatalogController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().NotBeEmpty();
        typeof(ErrorController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().NotBeEmpty();
    }

    [Fact]
    public void Account_profile_is_not_public()
    {
        var profile = typeof(AccountController).GetMethod(nameof(AccountController.Profile))!;
        profile.GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        profile.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().BeEmpty();
    }

    [Fact]
    public void Employee_self_service_requires_hr_module_entitlement()
    {
        var module = typeof(EmployeeSelfServiceController).GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>().Single();
        module.Policy.Should().Be(RequireModuleAttribute.PolicyPrefix + "HR");
    }

    [Theory]
    [InlineData(typeof(LibraryController), "LIBRARY")]
    [InlineData(typeof(TransportController), "TRANSPORT")]
    public void Operational_controllers_require_authentication_module_entitlement_and_antiforgery(Type controllerType, string moduleCode)
    {
        controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
        controllerType.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true).Should().NotBeEmpty();
        var module = controllerType.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>().Single();
        module.Policy.Should().Be(RequireModuleAttribute.PolicyPrefix + moduleCode);
    }

    [Theory]
    [InlineData(typeof(LibraryController), nameof(LibraryController.Issue), "Librarian")]
    [InlineData(typeof(LibraryController), nameof(LibraryController.Close), "Librarian")]
    [InlineData(typeof(TransportController), nameof(TransportController.Assign), "TransportManager")]
    [InlineData(typeof(TransportController), nameof(TransportController.Close), "TransportManager")]
    public void Operational_mutations_require_privileged_roles(Type controllerType, string actionName, string operationalRole)
    {
        var authorize = controllerType.GetMethod(actionName)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var roles = (authorize.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        roles.Should().Contain("TenantAdmin");
        roles.Should().Contain("Principal");
        roles.Should().Contain(operationalRole);
    }
}
