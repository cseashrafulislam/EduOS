using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;
public sealed class TenantCustomFieldSecurityTests
{
    [Fact]
    public void Api_is_tenant_admin_only_and_antiforgery_protected()
    {
        var type = typeof(CustomFieldsApiController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public async Task Unauthenticated_caller_cannot_list_definitions()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new CustomFieldService(null!, null!, null!, null!, user.Object, NullLogger<CustomFieldService>.Instance);
        var result = await service.GetDefinitionsAsync("Tenant", 1, 20);
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_caller_cannot_update_tenant_values()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new CustomFieldService(null!, null!, null!, null!, user.Object, NullLogger<CustomFieldService>.Instance);
        var result = await service.SaveValuesAsync("Tenant", 1, Array.Empty<EduOS.Core.DTOs.System.SaveCustomFieldValueRequestDto>());
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }
}
