using EduOS.Core.Entities.Communication;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Communication;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class NoticeArchiveBehaviorTests
{
    [Fact]
    public async Task Archive_is_tenant_scoped_idempotent_and_preserves_receipts()
    {
        await using var db = Database();
        var own = new Notice { TenantId = 101, Title = "Own", Body = "Body", RowVersion = [1, 2, 3] };
        var foreign = new Notice { TenantId = 202, Title = "Other", Body = "Body", RowVersion = [4, 5, 6] };
        db.Notices.AddRange(own, foreign);
        await TestTenantSeeder.SaveAsync(db);
        db.NoticeAudiences.Add(new NoticeAudience { TenantId = 101, NoticeId = own.Id, AudienceTypeCode = "All" });
        db.NoticeReadReceipts.Add(new NoticeReadReceipt { TenantId = 101, NoticeId = own.Id, UserId = 10 });
        await db.SaveChangesAsync();

        var service = Service(db, true);
        (await service.ArchiveNoticeAsync(foreign.PublicId, "invalid")).Success.Should().BeTrue();
        foreign.IsDeleted.Should().BeFalse();
        (await service.ArchiveNoticeAsync(own.PublicId, "invalid")).StatusCode.Should().Be(409);
        (await service.ArchiveNoticeAsync(own.PublicId, Convert.ToBase64String(own.RowVersion))).Success.Should().BeTrue();
        own.IsDeleted.Should().BeTrue();
        own.DeletedBy.Should().Be(10);
        own.DeletedAt.Should().NotBeNull();
        (await service.ArchiveNoticeAsync(own.PublicId, "invalid")).Success.Should().BeTrue();
        (await service.GetNoticesAsync(1, 20, null)).Data!.Items.Should().BeEmpty();
        (await db.NoticeReadReceipts.CountAsync(x => x.NoticeId == own.Id)).Should().Be(1);
        (await db.NoticeAudiences.CountAsync(x => x.NoticeId == own.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Archive_requires_tenant_admin()
    {
        await using var db = Database();
        var row = new Notice { TenantId = 101, Title = "Notice", Body = "Body" };
        db.Notices.Add(row);
        await db.SaveChangesAsync();
        (await Service(db, false).ArchiveNoticeAsync(row.PublicId, "invalid")).StatusCode.Should().Be(403);
        (await Service(db, true).ArchiveNoticeAsync(Guid.Empty, "invalid")).StatusCode.Should().Be(400);
        row.IsDeleted.Should().BeFalse();
    }

    private static EduOSDbContext Database()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("notice-archive-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    private static CommunicationService Service(EduOSDbContext db, bool admin) => new(
        db, new User(admin), new NoticeRepository(db), new NotificationRepository(db), db,
        new EphemeralDataProtectionProvider(), NullLogger<CommunicationService>.Instance);

    private sealed class User(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 10;
        public long TenantId => 101;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Student"];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
