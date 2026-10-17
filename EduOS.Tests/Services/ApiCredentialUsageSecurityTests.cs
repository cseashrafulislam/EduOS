using EduOS.App.Controllers.Api;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ApiCredentialUsageSecurityTests
{
    [Fact]
    public void Scope_must_be_exact()
    {
        Assert.True(ApiCredentialUsageService.HasScope("integration:read"));
        Assert.True(ApiCredentialUsageService.HasScope("webhooks:read,integration:read"));
        Assert.False(ApiCredentialUsageService.HasScope("integration:readall"));
        Assert.False(ApiCredentialUsageService.HasScope(null));
    }

    [Fact]
    public void Endpoint_is_read_only_and_rate_limited()
    {
        var type = typeof(ApiCredentialUsageApiController);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(type.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true));
        Assert.NotEmpty(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true));
        Assert.NotEmpty(type.GetMethod("Get")!.GetCustomAttributes(typeof(HttpGetAttribute), true));
    }
}
