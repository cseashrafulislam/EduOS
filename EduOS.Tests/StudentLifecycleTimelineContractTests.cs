using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class StudentLifecycleTimelineContractTests
{
    [Fact]
    public void Timeline_endpoint_is_tenant_admin_only_module_scoped_and_not_cached()
    {
        var type = typeof(StudentLifecycleTimelineController);
        var roles = Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Where(x => !string.IsNullOrEmpty(x.Roles)));
        Assert.Equal("TenantAdmin", roles.Roles);
        var module = Assert.Single(type.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>());
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "STUDENT", module.Policy);
        Assert.True(Assert.Single(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()).NoStore);
        Assert.Equal("ApiPolicy", Assert.Single(type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>()).PolicyName);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public void Timeline_is_read_only_and_uses_the_reusable_service_contract()
    {
        var method = typeof(StudentLifecycleTimelineController).GetMethod(nameof(StudentLifecycleTimelineController.Get))!;
        Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotNull(typeof(IStudentLifecycleTimelineService).GetMethod("GetAsync"));
        Assert.Null(typeof(StudentLifecycleTimelineDto).GetProperty("TenantId"));
        Assert.Null(typeof(StudentLifecycleEventDto).GetProperty("GuardianId"));
    }
}
