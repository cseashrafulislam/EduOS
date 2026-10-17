using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantFeatureRestrictionSecurityTests
{
    [Fact]
    public void Feature_restriction_endpoints_require_tenant_admin_and_antiforgery()
    {
        var api = typeof(TenantFeaturesApiController);
        Assert.Equal("TenantAdmin", Assert.Single(api.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(api.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Equal("TenantAdmin", Assert.Single(typeof(TenantFeaturesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public async Task Anonymous_user_cannot_read_or_change_feature_restrictions()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new TenantFeatureRestrictionService(null!, user.Object,
            NullLogger<TenantFeatureRestrictionService>.Instance);
        Assert.Equal(403, (await service.GetAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await service.SaveAsync(1, new() { Restricted = true, Reason = "Test" })).StatusCode);
    }
}
