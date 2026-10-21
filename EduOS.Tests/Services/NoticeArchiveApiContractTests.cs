using EduOS.App.Controllers.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class NoticeArchiveApiContractTests
{
    [Fact]
    public void Archive_is_tenant_admin_only_and_antiforgery_protected()
    {
        var controller = typeof(CommunicationWorkspaceApiController);
        Attribute.IsDefined(controller, typeof(AutoValidateAntiforgeryTokenAttribute)).Should().BeTrue();
        var method = controller.GetMethod(nameof(CommunicationWorkspaceApiController.ArchiveNotice))!;
        method.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
        method.GetCustomAttributes(typeof(HttpDeleteAttribute), false).Should().ContainSingle();
    }
}
