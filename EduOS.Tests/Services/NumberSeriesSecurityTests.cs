using EduOS.Core.DTOs.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Entities.System;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using System.Data;

namespace EduOS.Tests.Services;
public sealed class NumberSeriesSecurityTests
{
    private sealed class RevokeBeforeTransaction(EduOSDbContext db, Action revoke) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken token = default) => db.SaveChangesAsync(token);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken token = default) => ExecuteInTransactionAsync(operation, IsolationLevel.Serializable, token);
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
            IsolationLevel isolationLevel, CancellationToken token = default)
        {
            revoke();
            await db.SaveChangesAsync(token);
            return await operation(token);
        }
    }

    [Fact]
    public async Task Concurrent_revocation_before_transaction_prevents_configuration_and_number_consumption()
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 11L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("number-series-revocation-race-" + Guid.NewGuid().ToString("N"))
            .Options, new HttpContextAccessor { HttpContext = context });
        CentralPrivilegedTestIdentity.Seed(db, 11, 10);
        db.NumberSeries.Add(new NumberSeries { TenantId = 11, Key = "STUDENT",
            Prefix = "S-", Padding = 4, NextValue = 1, IsActive = true });
        await db.SaveChangesAsync();
        var membership = db.TenantMemberships.Single(x => x.UserId == 10);
        var actor = new Mock<ICurrentUserService>();
        actor.SetupGet(x => x.IsAuthenticated).Returns(true);
        actor.SetupGet(x => x.UserId).Returns(10);
        actor.SetupGet(x => x.TenantId).Returns(11);
        actor.SetupGet(x => x.IsTenantAdmin).Returns(true);
        var service = new NumberSeriesService(db, actor.Object,
            new RevokeBeforeTransaction(db, () => membership.Status = MembershipStatus.Suspended),
            NullLogger<NumberSeriesService>.Instance);

        var change = await service.SaveSeriesAsync(null, new SaveNumberSeriesRequestDto {
            Key = "CERTIFICATE", Prefix = "C-", Padding = 5, IsActive = true });
        Assert.Equal(403, change.StatusCode);
        Assert.False(db.NumberSeries.Any(x => x.Key == "CERTIFICATE"));
        membership.Status = MembershipStatus.Active;
        await db.SaveChangesAsync();

        var number = await service.IssueNumberAsync("STUDENT", Guid.NewGuid());
        Assert.Equal(403, number.StatusCode);
        Assert.Equal(1L, db.NumberSeries.Single(x => x.Key == "STUDENT").NextValue);
        Assert.False(db.IdempotencyRecords.Any());
    }

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
