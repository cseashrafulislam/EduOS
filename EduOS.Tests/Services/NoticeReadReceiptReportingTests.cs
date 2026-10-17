using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Communication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class NoticeReadReceiptReportingTests
{
    private static EduOSDbContext Database()
    {
        var context = new DefaultHttpContext(); context.Items["TenantId"] = 101L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("notice-receipts-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = context });
    }

    private static MessageWorkspaceService Service(EduOSDbContext db, bool admin)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.TenantId).Returns(101);
        user.SetupGet(x => x.UserId).Returns(10);
        user.SetupGet(x => x.IsTenantAdmin).Returns(admin);
        return new MessageWorkspaceService(db, user.Object, db);
    }

    [Fact]
    public async Task Admin_receipts_are_tenant_scoped_and_exclude_departed_members()
    {
        await using var db = Database();
        var notice = new Notice { Id = 900, TenantId = 101, Title = "Safety", Body = "Read", IsPublished = true };
        var other = new Notice { Id = 901, TenantId = 202, Title = "Private", Body = "Read", IsPublished = true };
        db.Notices.AddRange(notice, other);
        db.ApplicationUsers.AddRange(
            new ApplicationUser { Id = 10, UserName = "reader10", FullName = "Active Reader" },
            new ApplicationUser { Id = 20, UserName = "reader20", FullName = "Other Tenant" },
            new ApplicationUser { Id = 30, UserName = "reader30", FullName = "Former Member" });
        db.TenantMemberships.AddRange(
            new TenantMembership { TenantId = 101, UserId = 10, Status = MembershipStatus.Active },
            new TenantMembership { TenantId = 202, UserId = 20, Status = MembershipStatus.Active },
            new TenantMembership { TenantId = 101, UserId = 30, Status = MembershipStatus.Left });
        db.NoticeReadReceipts.AddRange(
            new NoticeReadReceipt { TenantId = 101, NoticeId = 900, UserId = 10 },
            new NoticeReadReceipt { TenantId = 202, NoticeId = 900, UserId = 20 },
            new NoticeReadReceipt { TenantId = 101, NoticeId = 900, UserId = 30 });
        await db.SaveChangesAsync();

        Assert.Equal(403, (await Service(db, false).GetNoticeReadReceiptsAsync(notice.PublicId, 1, 20)).StatusCode);
        Assert.Equal(404, (await Service(db, true).GetNoticeReadReceiptsAsync(other.PublicId, 1, 20)).StatusCode);
        Assert.Equal(400, (await Service(db, true).GetNoticeReadReceiptsAsync(notice.PublicId, 0, 20)).StatusCode);
        var result = await Service(db, true).GetNoticeReadReceiptsAsync(notice.PublicId, 1, 20);
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.TotalCount);
        Assert.Equal("Active Reader", Assert.Single(result.Data.Items).UserName);
    }

    [Fact]
    public void Read_receipt_endpoint_is_admin_only()
    {
        var method = typeof(CommunicationWorkspaceApiController).GetMethod("NoticeReadReceipts")!;
        var role = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", role.Roles);
        Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false));
    }
}
