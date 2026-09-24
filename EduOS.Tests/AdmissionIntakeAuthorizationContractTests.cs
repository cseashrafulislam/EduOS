using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class AdmissionIntakeAuthorizationContractTests
{
    [Fact]
    public void Management_controller_is_role_module_antiforgery_rate_limit_and_no_store_guarded()
    {
        var controller = typeof(AdmissionIntakeController);
        var authorize = controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single(x => !string.IsNullOrWhiteSpace(x.Roles));
        var module = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        var limiter = Assert.Single(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Cast<EnableRateLimitingAttribute>());
        var cache = Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());

        Assert.Equal("TenantAdmin,AdmissionOfficer", authorize.Roles);
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "ADMISSION", module.Policy);
        Assert.Equal("AdmissionIntakePolicy", limiter.PolicyName);
        Assert.True(cache.NoStore);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Empty(controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Public_document_upload_remains_anonymous_rate_limited_no_store_and_size_bounded()
    {
        var controller = typeof(PublicAdmissionsController);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true));
        Assert.True(Assert.Single(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>()).NoStore);
        var upload = controller.GetMethod(nameof(PublicAdmissionsController.UploadDocument))!;
        Assert.NotEmpty(upload.GetCustomAttributes(typeof(HttpPostAttribute), true));
        Assert.NotEmpty(upload.GetCustomAttributes(typeof(RequestSizeLimitAttribute), true));
    }
}
