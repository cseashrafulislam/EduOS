using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.App;

public sealed class GatewayProviderInspectionContractTests
{
    [Fact]
    public void Provider_inspection_reuses_payment_owner_and_requires_superadmin_antiforgery()
    {
        Assert.NotNull(typeof(ISubscriptionPaymentService).GetMethod(
            nameof(ISubscriptionPaymentService.InspectGatewayProviderAsync)));
        var controller = typeof(SubscriptionPaymentController);
        var action = controller.GetMethod(nameof(SubscriptionPaymentController.InspectGatewayProvider))!;
        Assert.NotNull(action);
        Assert.Contains(action.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>(), x => x.Template == "admin/gateway-payments/{paymentId:long}/inspect-provider");
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "SuperAdmin");
        Assert.Contains(action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>(), x => x.PolicyName == "ApiPolicy");
        Assert.Contains(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true)
            .Cast<AutoValidateAntiforgeryTokenAttribute>(), _ => true);
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Inspection_request_does_not_accept_provider_status_or_amount()
    {
        Assert.Equal(new[] { "RowVersion" },
            typeof(GatewayRecoveryRequestDto).GetProperties().Select(x => x.Name));
        var dto = new GatewayProviderInspectionDto();
        Assert.False(dto.ProviderConfirmed);
        Assert.False(dto.AmountMatches);
        Assert.False(dto.CurrencyMatches);
        Assert.NotNull(typeof(GatewayProviderInspectionDto).GetProperty(nameof(dto.EvidenceSha256)));
        Assert.DoesNotContain(typeof(GatewayProviderInspectionDto).GetProperties(),
            x => x.Name is "RawResponse" or "ProviderTransactionId" or "PayerAccountNumber");
    }
}
