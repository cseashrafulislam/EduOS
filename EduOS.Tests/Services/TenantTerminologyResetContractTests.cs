using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantTerminologyResetContractTests
{
    [Fact]
    public void Reset_actions_require_antiforgery_and_tenant_admin()
    {
        var mvc = typeof(TenantTerminologyController).GetMethod("Reset");
        Assert.NotNull(mvc);
        Assert.NotEmpty(mvc!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        var api = typeof(TenantSettingController).GetMethod("ResetTerminology");
        Assert.NotNull(api);
        Assert.NotEmpty(api!.GetCustomAttributes(typeof(HttpDeleteAttribute), true));
        var role = Assert.Single(api.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", role.Roles);
        Assert.NotNull(typeof(ITenantSettingService).GetMethod("RetireTerminologyAsync"));
    }
}
