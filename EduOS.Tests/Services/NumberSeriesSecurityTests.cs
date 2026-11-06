using EduOS.Core.DTOs.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task Tenant_context_mismatch_fails_closed_for_listing_configuration_and_issuance()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 22L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("number-series-mismatched-tenant-" + Guid.NewGuid().ToString("N"))
            .Options, accessor);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.IsTenantAdmin).Returns(true);
        user.SetupGet(x => x.TenantId).Returns(11L);
        var service = new NumberSeriesService(db, user.Object, null!,
            NullLogger<NumberSeriesService>.Instance);
        Assert.Equal(403, (await service.GetSeriesAsync(1, 20, null)).StatusCode);
        Assert.Equal(403, (await service.SaveSeriesAsync(null,
            new SaveNumberSeriesRequestDto { Key = "STUDENT" })).StatusCode);
        Assert.Equal(403, (await service.IssueNumberAsync("STUDENT", Guid.NewGuid())).StatusCode);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Number_series_configuration_requires_current_role_and_membership_but_respects_owner()
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 11L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("number-series-live-role-" + Guid.NewGuid().ToString("N"))
            .Options, new HttpContextAccessor { HttpContext = context });
        CentralPrivilegedTestIdentity.Seed(db, 11, 10);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(10);
        user.SetupGet(x => x.TenantId).Returns(11);
        user.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var service = new NumberSeriesService(db, user.Object, null!,
            NullLogger<NumberSeriesService>.Instance);

        Assert.Equal(200, (await service.GetSeriesAsync(1, 20, null)).StatusCode);
        var role = db.Roles.Single(x => x.Id == 9001);
        role.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.GetSeriesAsync(1, 20, null)).StatusCode);
        Assert.Equal(403, (await service.SaveSeriesAsync(null,
            new SaveNumberSeriesRequestDto { Key = "STUDENT" })).StatusCode);

        var membership = db.TenantMemberships.Single(x => x.UserId == 10);
        membership.IsOwner = true;
        await db.SaveChangesAsync();
        Assert.Equal(200, (await service.GetSeriesAsync(1, 20, null)).StatusCode);
        membership.Status = MembershipStatus.Suspended;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await service.GetSeriesAsync(1, 20, null)).StatusCode);
        Assert.Equal(403, (await service.IssueNumberAsync("STUDENT", Guid.NewGuid())).StatusCode);
    }
}
