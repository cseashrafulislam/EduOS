using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.App;

public sealed class AdmissionOfferWorkspaceContractTests
{
    [Fact]
    public void Dedicated_offer_workspace_is_tenant_admin_only()
    {
        var controller = typeof(AdmissionsController);
        var action = controller.GetMethod(nameof(AdmissionsController.Offers));
        Assert.NotNull(action);
        Assert.Equal("TenantAdmin", Assert.Single(action!.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.True(Assert.Single(action.GetCustomAttributes(typeof(ResponseCacheAttribute), false)
            .Cast<ResponseCacheAttribute>()).NoStore);
    }

    [Theory]
    [InlineData(nameof(AdmissionApplicationsController.IssueOffer), "{reference:guid}/offer")]
    [InlineData(nameof(AdmissionApplicationsController.RecordVerifiedAcceptance), "{reference:guid}/offer/verified-acceptance")]
    [InlineData(nameof(AdmissionApplicationsController.Admit), "{reference:guid}/admit")]
    public void Offer_acceptance_and_conversion_use_existing_api_routes(string actionName, string route)
    {
        var action = typeof(AdmissionApplicationsController).GetMethod(actionName);
        Assert.NotNull(action);
        Assert.Equal(route, Assert.Single(action!.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>()).Template);
    }

    [Fact]
    public void Verified_acceptance_requires_tenant_admin_and_canonical_service()
    {
        var action = typeof(AdmissionApplicationsController)
            .GetMethod(nameof(AdmissionApplicationsController.RecordVerifiedAcceptance));
        Assert.Equal("TenantAdmin", Assert.Single(action!.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.NotNull(typeof(IAdmissionEnrollmentService).GetMethod("IssueOfferAsync"));
        Assert.NotNull(typeof(IAdmissionEnrollmentService).GetMethod("RecordVerifiedAcceptanceAsync"));
        Assert.NotNull(typeof(IAdmissionEnrollmentService).GetMethod("AdmitAsync"));
    }
}
