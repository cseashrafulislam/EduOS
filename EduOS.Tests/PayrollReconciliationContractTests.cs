using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests;

public sealed class PayrollReconciliationContractTests
{
    [Fact]
    public void Reconciliation_api_requires_tenant_admin_finance_and_payroll()
    {
        var controller=typeof(PayrollReconciliationApiController);
        var attributes=controller.GetCustomAttributes(typeof(AuthorizeAttribute),true)
            .Cast<AuthorizeAttribute>().ToArray();
        Assert.Contains(attributes,x=>x.Roles=="TenantAdmin");
        Assert.Contains(attributes,x=>x.Policy==RequireModuleAttribute.PolicyPrefix+"PAYROLL");
        Assert.Contains(attributes,x=>x.Policy==RequireModuleAttribute.PolicyPrefix+"FINANCE");
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(ResponseCacheAttribute),true));
        Assert.NotNull(typeof(IPayrollReconciliationService).GetMethod("GetAsync"));
    }

    [Theory]
    [InlineData(false,false,false,100,0,0,"NotPosted")]
    [InlineData(true,true,false,100,0,0,"Outstanding")]
    [InlineData(true,true,false,100,0,30,"PendingVerification")]
    [InlineData(true,true,false,100,70,0,"Partial")]
    [InlineData(true,true,false,100,100,0,"Paid")]
    [InlineData(true,false,false,100,100,0,"NeedsReview")]
    [InlineData(true,true,true,100,100,0,"NeedsReview")]
    [InlineData(true,true,false,100,80,30,"NeedsReview")]
    public void Settlement_never_infers_paid_without_posted_evidence(
        bool posted,bool journalValid,bool bad,int net,int paid,int pending,string expected)
    {
        Assert.Equal(expected,PayrollReconciliationRules.Settlement(
            posted,journalValid,bad,net,paid,pending));
    }
}
