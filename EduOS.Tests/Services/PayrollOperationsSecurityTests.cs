using System.Linq;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollOperationsSecurityTests
{
    [Theory]
    [InlineData("CreateRun")]
    [InlineData("Runs")]
    [InlineData("Run")]
    [InlineData("Employees")]
    [InlineData("Approve")]
    [InlineData("Post")]
    [InlineData("Payment")]
    public void Tenant_wide_payroll_actions_require_tenant_admin(string action)
    {
        var method = typeof(HrPayrollController).GetMethod(action);
        Assert.NotNull(method);
        var rule = Assert.Single(method!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", rule.Roles);
    }

    [Fact]
    public void Payslips_remain_authenticated_self_service()
    {
        Assert.Contains(typeof(HrPayrollController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            rule => string.IsNullOrEmpty(rule.Roles));
        Assert.NotNull(typeof(HrPayrollController).GetMethod("MyPayslips"));
    }
}
