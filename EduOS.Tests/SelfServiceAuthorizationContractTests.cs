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
        Assert.Empty(type.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Theory]
    [InlineData(nameof(SelfServicePortalController.Attendance), "ATTENDANCE")]
    [InlineData(nameof(SelfServicePortalController.Results), "EXAM")]
    [InlineData(nameof(SelfServicePortalController.Fees), "FINANCE")]
    [InlineData(nameof(SelfServicePortalController.Transport), "TRANSPORT")]
    [InlineData(nameof(SelfServicePortalController.Homework), "LMS")]
    [InlineData(nameof(SelfServicePortalController.Assignments), "LMS")]
    public void Student_guardian_sensitive_reads_keep_module_entitlement(string actionName, string moduleCode)
    {
        var method = typeof(SelfServicePortalController).GetMethods().Single(x => x.Name == actionName);
        var module = Assert.Single(method.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var get = Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());

        Assert.Equal(RequireModuleAttribute.PolicyPrefix + moduleCode, module.Policy);
        Assert.False(string.IsNullOrWhiteSpace(get.Template));
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
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
        Assert.Empty(type.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Employee_leave_application_remains_post_only_and_not_anonymous()
    {
        var method = typeof(EmployeeSelfServiceController).GetMethods().Single(x => x.Name == nameof(EmployeeSelfServiceController.ApplyLeave));
        var post = Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());

        Assert.Equal("leave", post.Template);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
