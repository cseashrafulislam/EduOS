using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class AcademicCalendarAuthorizationContractTests
{
    [Fact]
    public void Calendar_controller_keeps_module_antiforgery_rate_limit_and_no_store_boundaries()
    {
        var controller = typeof(AcademicCalendarsController);
        var roleBoundary = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Where(x => !string.IsNullOrWhiteSpace(x.Roles)));
        var module = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());
        var cache = Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());

        Assert.Contains("Student", roleBoundary.Roles);
        Assert.Contains("Guardian", roleBoundary.Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ACADEMIC", module.Policy);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.True(cache.NoStore);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Theory]
    [InlineData(nameof(AcademicCalendarsController.SavePolicy))]
    [InlineData(nameof(AcademicCalendarsController.CreateEvent))]
    [InlineData(nameof(AcademicCalendarsController.UpdateEvent))]
    public void Calendar_mutations_are_post_only_and_manager_scoped(string methodName)
    {
        var method = typeof(AcademicCalendarsController).GetMethods().Single(x => x.Name == methodName);
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin,Principal,VicePrincipal", authorize.Roles);
        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
