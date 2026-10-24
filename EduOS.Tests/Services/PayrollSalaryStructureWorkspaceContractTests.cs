using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollSalaryStructureWorkspaceContractTests
{
    [Fact]
    public void History_uses_existing_payroll_service_and_canonical_entities()
    {
        Assert.True(typeof(IPayrollAdministrationService).IsAssignableFrom(typeof(HrPayrollService)));
        Assert.NotNull(typeof(IPayrollAdministrationService).GetMethod("GetSalaryStructuresAsync"));
        Assert.NotNull(typeof(HrPayrollService).GetMethod("GetSalaryStructuresAsync"));
    }

    [Fact]
    public void Salary_history_api_requires_tenant_admin_and_payroll_module()
    {
        var api=typeof(EduOS.App.Controllers.Api.HrPayrollController);
        Assert.Contains(api.GetCustomAttributes(typeof(RequireModuleAttribute),true)
            .Cast<RequireModuleAttribute>(),x=>x.Policy=="EduOSModule:PAYROLL");
        var action=api.GetMethod("SalaryStructureHistory");
        Assert.NotNull(action);
        Assert.Equal("TenantAdmin",Assert.Single(action!.GetCustomAttributes(typeof(AuthorizeAttribute),true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.Equal("salary-structures/{employeeReference:guid}",
            Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute),true)
                .Cast<HttpGetAttribute>()).Template);
    }

    [Fact]
    public void Salary_history_workspace_is_tenant_admin_only()
    {
        var mvc=typeof(EduOS.App.Controllers.PayrollSalaryStructuresController);
        Assert.Equal("TenantAdmin",Assert.Single(mvc.GetCustomAttributes(typeof(AuthorizeAttribute),true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.Contains(mvc.GetCustomAttributes(typeof(RequireModuleAttribute),true)
            .Cast<RequireModuleAttribute>(),x=>x.Policy=="EduOSModule:PAYROLL");
    }
}
