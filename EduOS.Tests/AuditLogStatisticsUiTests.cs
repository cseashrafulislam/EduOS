using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests;

public sealed class AuditLogStatisticsUiTests
{
    [Fact]
    public void Existing_statistics_endpoint_is_role_restricted_and_read_only()
    {
        var controller = typeof(AuditLogController);
        var authorize = Attribute.GetCustomAttribute(controller, typeof(AuthorizeAttribute)) as AuthorizeAttribute;
        Assert.NotNull(authorize);
        Assert.Equal("TenantAdmin,Principal", authorize!.Roles);
        var method = controller.GetMethod(nameof(AuditLogController.Statistics));
        Assert.NotNull(method);
        var get = Attribute.GetCustomAttribute(method!, typeof(HttpGetAttribute)) as HttpGetAttribute;
        Assert.NotNull(get);
        Assert.Equal("statistics", get!.Template);
    }
}
