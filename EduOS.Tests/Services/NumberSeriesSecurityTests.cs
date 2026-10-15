using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;
public sealed class NumberSeriesSecurityTests
{
    [Fact]
    public async Task Unauthenticated_user_cannot_list_series()
    {
        var user=new Mock<ICurrentUserService>();
        var svc=new NumberSeriesService(null!,user.Object,null!,
            NullLogger<NumberSeriesService>.Instance);
        var response=await svc.GetSeriesAsync(1,20,null);
        Assert.Equal(403,response.StatusCode);
    }
    [Fact]
    public async Task Tenant_admin_without_tenant_cannot_create_series()
    {
        var user=new Mock<ICurrentUserService>();
        user.SetupGet(x=>x.IsAuthenticated).Returns(true);
        user.SetupGet(x=>x.IsTenantAdmin).Returns(true);
        var svc=new NumberSeriesService(null!,user.Object,null!,
            NullLogger<NumberSeriesService>.Instance);
        var response=await svc.SaveSeriesAsync(null,new SaveNumberSeriesRequestDto{Key="STUDENT"});
        Assert.Equal(403,response.StatusCode);
    }
    [Fact]
    public async Task Missing_request_id_cannot_issue_number()
    {
        var user=new Mock<ICurrentUserService>();
        user.SetupGet(x=>x.IsAuthenticated).Returns(true);
        user.SetupGet(x=>x.TenantId).Returns(11);
        var svc=new NumberSeriesService(null!,user.Object,null!,
            NullLogger<NumberSeriesService>.Instance);
        var response=await svc.IssueNumberAsync("STUDENT",Guid.Empty);
        Assert.Equal(400,response.StatusCode);
    }
}
