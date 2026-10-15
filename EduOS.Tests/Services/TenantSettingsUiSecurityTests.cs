using EduOS.App.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantSettingsUiSecurityTests
{
    [Fact]
    public void Settings_page_requires_tenant_admin_and_disables_cache()
    {
        var auth = Assert.Single(typeof(TenantSettingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", auth.Roles);
        var cache = Assert.Single(typeof(TenantSettingsController)
            .GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());
        Assert.True(cache.NoStore);
    }

    [Fact]
    public void Settings_save_requires_antiforgery()
    {
        var save = typeof(TenantSettingsController).GetMethod(nameof(TenantSettingsController.Save));
        Assert.NotNull(save);
        Assert.NotEmpty(save!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
    }
}
