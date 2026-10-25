using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class SubscriptionPaymentReceiptContractTests
{
    [Fact]
    public void Receipt_is_a_tenant_admin_only_read_operation()
    {
        var endpoint = typeof(SubscriptionPaymentController).GetMethod(nameof(SubscriptionPaymentController.GetReceipt))!;
        Assert.Equal("TenantAdmin", Assert.Single(endpoint.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.Equal("receipts/{paymentId:long}", Assert.Single(endpoint.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>()).Template);
        Assert.True(Assert.Single(endpoint.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()).NoStore);
        var mvc = typeof(SubscriptionBillingController);
        Assert.Equal("TenantAdmin", Assert.Single(mvc.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.True(Assert.Single(mvc.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()).NoStore);
        Assert.NotNull(typeof(ISubscriptionPaymentService).GetMethod(nameof(ISubscriptionPaymentService.GetReceiptAsync)));
    }

    [Fact]
    public void Receipt_dto_exposes_no_payer_bank_account_or_deposit_slip()
    {
        var names = typeof(SubscriptionPaymentReceiptDto).GetProperties().Select(x => x.Name).ToArray();
        Assert.Contains(nameof(SubscriptionPaymentReceiptDto.InvoiceNumber), names);
        Assert.Contains(nameof(SubscriptionPaymentReceiptDto.CompletedAtUtc), names);
        Assert.DoesNotContain("PayerAccountNumber", names);
        Assert.DoesNotContain("DepositSlipFileId", names);
        Assert.DoesNotContain("VerificationNote", names);
    }
}
