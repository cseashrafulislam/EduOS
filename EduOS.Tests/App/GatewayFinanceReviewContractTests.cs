using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;
using Xunit;

namespace EduOS.Tests.App;

public sealed class GatewayFinanceReviewContractTests
{
    [Fact]
    public void Finance_review_reuses_payment_owner_and_enforces_superadmin_antiforgery()
    {
        Assert.NotNull(typeof(ISubscriptionPaymentService).GetMethod(nameof(ISubscriptionPaymentService.GetGatewayFinanceHistoryAsync)));
        Assert.NotNull(typeof(ISubscriptionPaymentService).GetMethod(nameof(ISubscriptionPaymentService.RecordGatewayFinanceReviewAsync)));
        var controller = typeof(SubscriptionPaymentController);
        var action = controller.GetMethod(nameof(SubscriptionPaymentController.RecordGatewayFinanceReview))!;
        Assert.NotNull(action);
        Assert.Contains(action.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>(), x => x.Template == "admin/gateway-payments/{paymentId:long}/finance-review");
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "SuperAdmin");
        Assert.Contains(action.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)
            .Cast<EnableRateLimitingAttribute>(), x => x.PolicyName == "ApiPolicy");
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Fact]
    public void Finance_history_is_read_only_and_superadmin_only()
    {
        var action = typeof(SubscriptionPaymentController).GetMethod(nameof(SubscriptionPaymentController.GetGatewayFinanceHistory))!;
        Assert.Contains(action.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>(), x => x.Template == "admin/gateway-payments/{paymentId:long}/finance-history");
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "SuperAdmin");
    }

    [Fact]
    public void Finance_review_input_never_accepts_provider_or_settlement_values()
    {
        var names = typeof(GatewayFinanceReviewRequestDto).GetProperties().Select(p => p.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Decision", "EvidenceAuditId", "ExternalCaseReference", "Note", "RequestId", "RowVersion" }, names);
        Assert.DoesNotContain(typeof(GatewayFinanceHistoryItemDto).GetProperties(),
            x => x.Name is "RawResponse" or "PayerAccountNumber" or "SettlementAmount");
    }

    private static GatewayFinanceHistoryItemDto Map(AuditLog log)
    {
        var method = typeof(SubscriptionPaymentService).GetMethod("MapFinanceAudit",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (GatewayFinanceHistoryItemDto)method!.Invoke(null, [log])!;
    }

    [Fact]
    public void Escalation_history_exposes_fingerprint_but_not_raw_provider_payload()
    {
        var item = Map(new AuditLog
        {
            Id = 22, EntityId = 7, Action = "GatewayProviderEscalation", IsSuccess = true,
            NewValue = "{\"Status\":\"Successful\",\"RequiresManualReview\":true,\"EvidenceSha256\":\"ABCDEF\"}"
        });
        Assert.Equal(7L, item.PaymentId);
        Assert.True(item.ProviderConfirmed);
        Assert.True(item.RequiresManualReview);
        Assert.Equal("ABCDEF", item.EvidenceSha256);
        Assert.Null(item.Note);
    }

    [Fact]
    public void Finance_review_audit_does_not_masquerade_as_provider_confirmation()
    {
        var item = Map(new AuditLog
        {
            Action = "GatewayFinanceReview", IsSuccess = true,
            NewValue = "{\"Decision\":\"Investigating\",\"Note\":\"Provider contacted\",\"EvidenceAuditId\":22,\"RequiresManualReview\":true}"
        });
        Assert.False(item.ProviderConfirmed);
        Assert.Equal("Investigating", item.Decision);
        Assert.Equal("Provider contacted", item.Note);
        Assert.Equal(22L, item.EvidenceAuditId);
    }

    [Fact]
    public void Administrative_closure_is_visible_but_never_provider_confirmation()
    {
        var item = Map(new AuditLog
        {
            Action = "GatewayFinanceReview", IsSuccess = true,
            NewValue = "{\"Decision\":\"ExternalCaseClosed\",\"Note\":\"Provider investigation closed externally\",\"CaseClosed\":true,\"ExternalCaseReference\":\"AMAR-123456\",\"EvidenceSha256\":\"ABCDEF\"}"
        });
        Assert.True(item.CaseClosed);
        Assert.Equal("ExternalCaseClosed", item.Decision);
        Assert.Equal("AMAR-123456", item.ExternalCaseReference);
        Assert.False(item.ProviderConfirmed);
        Assert.Null(Map(new AuditLog { Action = "GatewayProviderInspection",
            NewValue = "{\"CaseClosed\":true,\"ExternalCaseReference\":\"FAKE123\"}" }).ExternalCaseReference);
    }

    [Fact]
    public void Malformed_legacy_audit_does_not_break_finance_history()
    {
        var item = Map(new AuditLog { Action = "GatewayProviderInspection", NewValue = "legacy-not-json" });
        Assert.Null(item.EvidenceSha256);
        Assert.Null(item.Note);
    }
}
