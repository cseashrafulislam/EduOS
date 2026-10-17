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

public sealed class TenantFeatureAuditSecurityTests
{
    [Fact]
    public void Feature_audit_API_and_MVC_require_tenant_admin()
    {
        Assert.Equal("TenantAdmin", Assert.Single(typeof(TenantFeatureAuditApiController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(typeof(TenantFeatureAuditApiController)
            .GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Equal("TenantAdmin", Assert.Single(typeof(TenantFeatureAuditController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public async Task Unauthenticated_user_cannot_read_feature_audit()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new TenantFeatureAuditService(null!, user.Object,
            NullLogger<TenantFeatureAuditService>.Instance);
        Assert.Equal(403, (await service.GetAsync(1, 20)).StatusCode);
    }
}
