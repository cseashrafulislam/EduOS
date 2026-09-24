using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class StudentAttendanceAuthorizationContractTests
{
    [Fact]
    public void Attendance_controller_keeps_role_module_antiforgery_rate_limit_and_no_store_boundaries()
    {
        var controller = typeof(StudentAttendanceController);
        var roleBoundary = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Where(x => !string.IsNullOrWhiteSpace(x.Roles)));
        var module = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());
        var cache = Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());

        Assert.Equal("TenantAdmin,Principal,VicePrincipal,Teacher", roleBoundary.Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ATTENDANCE", module.Policy);
        Assert.Equal("ApiPolicy", limiter.PolicyName);
        Assert.True(cache.NoStore);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public void Attendance_save_is_post_only_and_not_anonymous()
    {
        var method = typeof(StudentAttendanceController).GetMethods().Single(x => x.Name == nameof(StudentAttendanceController.Save));

        Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Attendance_roster_is_get_only_and_not_anonymous()
    {
        var method = typeof(StudentAttendanceController).GetMethods().Single(x => x.Name == nameof(StudentAttendanceController.GetRoster));

        Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
