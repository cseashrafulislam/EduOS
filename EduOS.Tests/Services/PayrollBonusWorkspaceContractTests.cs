using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollBonusWorkspaceContractTests
{
    [Fact]
    public void Canonical_bonus_register_service_is_exposed_through_existing_payroll_interface()
    {
        Assert.True(typeof(IPayrollAdministrationService).IsAssignableFrom(typeof(HrPayrollService)));
        var method=typeof(IPayrollAdministrationService).GetMethod("GetBonusesAsync");
        Assert.NotNull(method);
        Assert.NotNull(typeof(HrPayrollService).GetMethod("GetBonusesAsync"));
    }

    [Fact]
    public void Bonus_workspace_requires_payroll_module_and_tenant_admin()
    {
        var mvc=typeof(EduOS.App.Controllers.PayrollBonusesController);
        Assert.Contains(mvc.GetCustomAttributes(typeof(AuthorizeAttribute),true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        Assert.Contains(mvc.GetCustomAttributes(typeof(RequireModuleAttribute),true)
            .Cast<RequireModuleAttribute>(),x=>x.Policy=="EduOSModule:PAYROLL");
        var api=typeof(EduOS.App.Controllers.Api.HrPayrollController);
        var action=api.GetMethod("Bonuses");
        Assert.NotNull(action);
        Assert.Equal("TenantAdmin",Assert.Single(action!.GetCustomAttributes(typeof(AuthorizeAttribute),true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.Equal("bonuses",Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute),true)
            .Cast<HttpGetAttribute>()).Template);
    }
}
