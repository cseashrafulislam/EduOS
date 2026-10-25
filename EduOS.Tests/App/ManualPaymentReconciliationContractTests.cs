using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.App;

public sealed class ManualPaymentReconciliationContractTests
{
    [Fact]
    public void History_page_and_api_are_platform_administrator_only()
    {
        var page = typeof(SubscriptionPaymentHistoryController);
        Assert.Contains(page.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "SuperAdmin");
        var action = typeof(SubscriptionPaymentController)
            .GetMethod(nameof(SubscriptionPaymentController.GetReconciliationHistory));
        Assert.NotNull(action);
        Assert.Contains(action!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "SuperAdmin");
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Contains(action.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>(), x => x.Template == "admin/reconciliation-history");
    }

    [Fact]
    public void Reconciliation_uses_existing_payment_service_owner_and_read_only_dto()
    {
        var method = typeof(ISubscriptionPaymentService)
            .GetMethod(nameof(ISubscriptionPaymentService.GetManualPaymentReconciliationHistoryAsync));
        Assert.NotNull(method);
        Assert.NotNull(typeof(SubscriptionPaymentService).GetMethod(method!.Name));
        Assert.Equal(typeof(Task<EduOS.Core.Common.ApiResponse<PagedManualPaymentReconciliationDto>>),
            method.ReturnType);
        Assert.DoesNotContain(typeof(ManualPaymentReconciliationDto).GetProperties(),
            property => property.Name is "PayerAccountNumber" or "DepositSlipFileId");
    }
}
