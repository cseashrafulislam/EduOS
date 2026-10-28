using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollTenantWideAuthorizationTests
{
    [Theory]
    [InlineData(nameof(HrPayrollController.EmployeeOptions))]
    [InlineData(nameof(HrPayrollController.Components))]
    [InlineData(nameof(HrPayrollController.SaveComponent))]
    [InlineData(nameof(HrPayrollController.SaveSalary))]
    [InlineData(nameof(HrPayrollController.Bonus))]
    [InlineData(nameof(HrPayrollController.Loan))]
    public void Institution_wide_payroll_endpoints_require_tenant_admin(string actionName)
    {
        var action = typeof(HrPayrollController).GetMethod(actionName);
        Assert.NotNull(action);
        var grant = Assert.Single(action!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", grant.Roles);
    }
}
