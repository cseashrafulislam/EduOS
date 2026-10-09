using EduOS.App.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests;

public sealed class AuditLogUiContractTests
{
    [Fact]
    public void Audit_page_is_restricted_to_the_same_roles_as_the_existing_api()
    {
        var authorization = Attribute.GetCustomAttribute(typeof(AuditLogsController), typeof(AuthorizeAttribute)) as AuthorizeAttribute;
        Assert.NotNull(authorization);
        Assert.Equal("TenantAdmin,Principal", authorization!.Roles);
        var action = typeof(AuditLogsController).GetMethod(nameof(AuditLogsController.Index));
        Assert.NotNull(action);
        Assert.NotNull(Attribute.GetCustomAttribute(action!, typeof(HttpGetAttribute)));
        Assert.IsType<ViewResult>(new AuditLogsController().Index());
    }
}
