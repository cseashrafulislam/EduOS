using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.App;

public sealed class TenantUserSecurityContractsTests
{
    [Fact]
    public void Campus_mutation_is_protected_and_requires_a_membership_version()
    {
        var controller = typeof(TenantUsersController);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var method = controller.GetMethod(nameof(TenantUsersController.SetCampuses))!;
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.True(method.GetCustomAttributes(typeof(HttpPutAttribute), true).Any());
        Assert.NotNull(typeof(SetMembershipCampusAccessRequestDto).GetProperty(nameof(SetMembershipCampusAccessRequestDto.MembershipRowVersion)));
        Assert.Contains(typeof(ITenantUserAdministrationService).GetMethods(),
            x => x.Name == nameof(ITenantUserAdministrationService.SetMemberCampusesAsync));
    }

    [Fact]
    public void Invitations_only_allow_anonymous_acceptance_not_admin_invite()
    {
        var controller = typeof(TenantUsersController);
        var send = controller.GetMethod(nameof(TenantUsersController.SendInvitation))!;
        var accept = controller.GetMethod(nameof(TenantUsersController.AcceptInvitation))!;
        Assert.Empty(send.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(accept.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public void Permission_policy_preserves_explicit_module_policy()
    {
        var perm = new RequirePermissionAttribute("student.read");
        Assert.Equal("EduOSPermission:STUDENT.READ", perm.Policy);
        var module = new RequireModuleAttribute("STUDENT");
        Assert.Equal("EduOSModule:STUDENT", module.Policy);
    }
}
