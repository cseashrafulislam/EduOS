using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.App;

public sealed class GatewayPaymentExceptionQueueContractTests
{
    [Fact]
    public void Exception_queue_reuses_canonical_payment_owner_and_is_superadmin_only()
    {
        Assert.NotNull(typeof(ISubscriptionPaymentService).GetMethod(
            nameof(ISubscriptionPaymentService.GetGatewayExceptionQueueAsync)));
        var action = typeof(SubscriptionPaymentController).GetMethod(
            nameof(SubscriptionPaymentController.GetGatewayExceptionQueue))!;
        Assert.NotNull(action);
        Assert.Contains(action.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>(), x => x.Template == "admin/gateway-exceptions");
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "SuperAdmin");
        Assert.Contains(action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>(), x => x.PolicyName == "ApiPolicy");
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Exception_triage_is_diagnostic_and_does_not_accept_payment_outcome()
    {
        var dto = new GatewayPaymentRecoveryDto();
        Assert.Equal("Awaiting", dto.ExceptionCategory);
        Assert.False(dto.CanReconcile);
        Assert.Equal(0, dto.AgeHours);
        Assert.NotNull(typeof(GatewayPaymentRecoveryDto).GetProperty(nameof(dto.ExceptionReason)));
        Assert.DoesNotContain(typeof(GatewayRecoveryRequestDto).GetProperties(),
            x => x.Name is "PaymentState" or "InvoiceState" or "ProviderOutcome" or "Amount");
    }
}
