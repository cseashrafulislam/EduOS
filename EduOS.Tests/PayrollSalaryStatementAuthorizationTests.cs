using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollSalaryStatementAuthorizationTests
{
    [Fact]
    public void Statement_api_is_tenant_admin_only_module_gated_and_rate_limited()
    {
        var controller = typeof(PayrollSalaryStatementController);
        var authorization = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", authorization.Roles);
        var module = Assert.Single(controller.GetCustomAttributes(typeof(RequireModuleAttribute), true).Cast<RequireModuleAttribute>());
        Assert.Equal(RequireModuleAttribute.PolicyPrefix + "PAYROLL", module.Policy);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true));
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(ResponseCacheAttribute), true));
    }
}
