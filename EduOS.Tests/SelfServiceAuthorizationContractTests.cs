using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class SelfServiceAuthorizationContractTests
{
    [Fact]
    public void Student_guardian_portal_keeps_role_and_rate_limit_boundary()
    {
        var type = typeof(SelfServicePortalController);
        var authorize = Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var limiter = Assert.Single(type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());

        Assert.Equal("Student,Guardian,Parent", authorize.Roles);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
    }

    [Fact]
    public void Employee_portal_keeps_role_module_antiforgery_and_rate_limit_boundary()
    {
        var type = typeof(EmployeeSelfServiceController);
        var authorize = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single(x => x.GetType() == typeof(AuthorizeAttribute));
        var module = Assert.Single(type.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());

        Assert.Equal("Teacher,Staff", authorize.Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "HR", module.Policy);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }
}
