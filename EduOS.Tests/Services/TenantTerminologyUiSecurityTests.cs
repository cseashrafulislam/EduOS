using EduOS.App.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantTerminologyUiSecurityTests
{
    [Fact]
    public void Terminology_page_is_tenant_admin_only()
    {
        var auth = Assert.Single(typeof(TenantTerminologyController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", auth.Roles);
    }

    [Fact]
    public void Terminology_save_requires_antiforgery()
    {
        var method = typeof(TenantTerminologyController).GetMethod("Save");
        Assert.NotNull(method);
        Assert.NotEmpty(method!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.NotNull(typeof(TenantTerminologyController).GetMethod("Index"));
    }
}
