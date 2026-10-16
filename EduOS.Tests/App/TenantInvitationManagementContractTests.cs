using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.App;

public sealed class TenantInvitationManagementContractTests
{
    [Fact]
    public void Invitation_history_and_revocation_require_tenant_admin_and_antiforgery()
    {
        var controller = typeof(TenantUsersController);
        Assert.Contains(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        foreach (var method in new[] { nameof(TenantUsersController.Invitations), nameof(TenantUsersController.RevokeInvitation) })
        {
            var action = controller.GetMethod(method)!;
            Assert.NotNull(action);
            Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }
    }

    [Fact]
    public void Invitation_management_reuses_canonical_contract_without_exposing_tokens()
    {
        Assert.NotNull(typeof(ITenantUserAdministrationService).GetMethod("GetInvitationsAsync"));
        Assert.NotNull(typeof(ITenantUserAdministrationService).GetMethod("RevokeInvitationAsync"));
        Assert.Null(typeof(TenantInvitationDto).GetProperty("TokenHash"));
        Assert.Null(typeof(TenantInvitationDto).GetProperty("Token"));
        Assert.NotNull(typeof(TenantInvitationDto).GetProperty("RowVersion"));
        Assert.NotNull(typeof(RevokeTenantInvitationRequestDto).GetProperty("RowVersion"));
    }
}
