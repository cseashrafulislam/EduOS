using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.App;

public sealed class GatewayPaymentRecoveryContractTests
{
    [Fact]
    public void Gateway_recovery_uses_existing_service_and_is_superadmin_only()
    {
        var service = typeof(ISubscriptionPaymentService);
        Assert.NotNull(service.GetMethod(nameof(ISubscriptionPaymentService.GetPendingGatewayRecoveryAsync)));
        Assert.NotNull(service.GetMethod(nameof(ISubscriptionPaymentService.ReconcileGatewayPaymentAsync)));
        Assert.NotNull(typeof(SubscriptionPaymentService).GetMethod(nameof(ISubscriptionPaymentService.ReconcileGatewayPaymentAsync)));
        var controller = typeof(SubscriptionPaymentController);
        foreach (var name in new[] { nameof(SubscriptionPaymentController.GetPendingGatewayRecovery),
                                     nameof(SubscriptionPaymentController.ReconcileGatewayPayment) })
        {
            var action = controller.GetMethod(name)!;
            Assert.NotNull(action);
            Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>(), attribute => attribute.Roles == "SuperAdmin");
            Assert.Contains(action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
                .Cast<EnableRateLimitingAttribute>(), attribute => attribute.PolicyName == "ApiPolicy");
            Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }
        Assert.Contains(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true)
            .Cast<AutoValidateAntiforgeryTokenAttribute>(), _ => true);
        Assert.Contains(typeof(SubscriptionPaymentHistoryController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(),
            attribute => attribute.Roles == "SuperAdmin");
    }

    [Fact]
    public void Recovery_request_cannot_supply_payment_amount_or_provider_outcome()
    {
        Assert.Equal(new[] { "RowVersion" },
            typeof(GatewayRecoveryRequestDto).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain(typeof(GatewayPaymentRecoveryDto).GetProperties(),
            x => x.Name is "PayerAccountNumber" or "DepositSlipFileId" or "ProviderTransactionId");
        var action = typeof(SubscriptionPaymentController)
            .GetMethod(nameof(SubscriptionPaymentController.ReconcileGatewayPayment))!;
        Assert.Contains(action.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>(), x => x.Template == "admin/gateway-payments/{paymentId:long}/reconcile");
    }
}
