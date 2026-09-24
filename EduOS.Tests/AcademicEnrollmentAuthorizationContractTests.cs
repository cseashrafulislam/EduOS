using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class AcademicEnrollmentAuthorizationContractTests
{
    [Fact]
    public void Enrollment_controller_keeps_module_antiforgery_rate_limit_and_no_store_boundaries()
    {
        var controller = typeof(AcademicEnrollmentsController);
        var authorize = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Where(x => !string.IsNullOrWhiteSpace(x.Roles)));
        var module = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());
        var cache = Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());

        Assert.Contains("Student", authorize.Roles);
        Assert.Contains("Guardian", authorize.Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ACADEMIC", module.Policy);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.True(cache.NoStore);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Theory]
    [InlineData(nameof(AcademicEnrollmentsController.Enroll), "TenantAdmin,Principal,VicePrincipal")]
    [InlineData(nameof(AcademicEnrollmentsController.RequestOptionalSubject), "Student,Guardian,Parent")]
    [InlineData(nameof(AcademicEnrollmentsController.DecideSubject), "TenantAdmin,Principal,VicePrincipal")]
    public void Enrollment_mutations_are_post_only_and_role_scoped(string methodName, string roles)
    {
        var method = typeof(AcademicEnrollmentsController).GetMethods().Single(x => x.Name == methodName);
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
        Assert.Equal(roles, authorize.Roles);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
