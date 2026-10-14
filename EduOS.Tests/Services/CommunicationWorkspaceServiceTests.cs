using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Communication;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CommunicationWorkspaceServiceTests
{
    [Fact]
    public async Task Reader_sees_only_published_targeted_notices_and_never_drafts()
    {
        await using var db = Database();
        db.TenantMemberships.Add(new TenantMembership { TenantId = 101, UserId = 10, Status = MembershipStatus.Active });
        db.Notices.AddRange(
            new Notice { Id = 1, TenantId = 101, Title = "All", Body = "Public", IsPublished = true, PublishAt = DateTime.UtcNow.AddMinutes(-2) },
            new Notice { Id = 2, TenantId = 101, Title = "Other", Body = "Secret", IsPublished = true, PublishAt = DateTime.UtcNow.AddMinutes(-2) },
            new Notice { Id = 3, TenantId = 101, Title = "Draft", Body = "Unpublished", IsPublished = false, PublishAt = DateTime.UtcNow.AddMinutes(-2) });
        db.NoticeAudiences.AddRange(
            new NoticeAudience { TenantId = 101, NoticeId = 1, AudienceTypeCode = "All" },
            new NoticeAudience { TenantId = 101, NoticeId = 2, AudienceTypeCode = "User", UserId = 11 },
            new NoticeAudience { TenantId = 101, NoticeId = 3, AudienceTypeCode = "All" });
        await db.SaveChangesAsync();
        var service = Service(db, false);
        var page = await service.GetNoticesAsync(1, 20, null);
        page.Success.Should().BeTrue(page.Message);
        page.Data!.Items.Select(x => x.Title).Should().Equal("All");
        (await service.GetNoticesAsync(1, 20, false)).StatusCode.Should().Be(403);
        (await service.GetNoticesAsync(0, 200, null)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Inbox_is_scoped_to_authenticated_user_and_tenant()
    {
        await using var db = Database();
        db.Notifications.AddRange(
            new Notification { TenantId = 101, UserId = 10, Title = "Mine", Body = "A" },
            new Notification { TenantId = 101, UserId = 11, Title = "Someone else", Body = "B" });
        await db.SaveChangesAsync();
        var page = await Service(db, false).GetMyNotificationsAsync(1, 20);
        page.Data!.Items.Select(x => x.Title).Should().Equal("Mine");
        (await Service(db, false).MarkNotificationReadAsync(99999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public void Api_requires_authentication_and_antiforgery_for_writes()
    {
        var type = typeof(CommunicationWorkspaceApiController);
        Attribute.IsDefined(type, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)).Should().BeTrue();
        Attribute.IsDefined(type, typeof(AutoValidateAntiforgeryTokenAttribute)).Should().BeTrue();
        var save = type.GetMethod(nameof(CommunicationWorkspaceApiController.CreateNotice))!;
        save.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
    }


    [Fact]
    public async Task Preferences_are_scoped_to_current_tenant_and_user()
    {
        await using var db = Database();
        db.NotificationPreferences.AddRange(
            new NotificationPreference { TenantId = 101, UserId = 10, Channel = NotificationChannelType.Email, EventCode = "EXAM.RESULT", IsEnabled = false },
            new NotificationPreference { TenantId = 101, UserId = 11, Channel = NotificationChannelType.Email, EventCode = "OTHER.USER", IsEnabled = true },
            new NotificationPreference { TenantId = 202, UserId = 10, Channel = NotificationChannelType.Push, EventCode = "OTHER.TENANT", IsEnabled = true });
        await db.SaveChangesAsync();
        var result = await Service(db, false).GetMyPreferencesAsync();
        result.Success.Should().BeTrue(result.Message);
        result.Data!.Select(x => x.EventCode).Should().Equal("EXAM.RESULT");
    }

    [Fact]
    public async Task Preference_write_rejects_invalid_event_and_stale_version()
    {
        await using var db = Database();
        var service = Service(db, false);
        var invalid = await service.SaveMyPreferenceAsync(new SaveNotificationPreferenceRequestDto
            { Channel = NotificationChannelType.Email, EventCode = "bad code", IsEnabled = true });
        invalid.Success.Should().BeFalse();
        var created = await service.SaveMyPreferenceAsync(new SaveNotificationPreferenceRequestDto
            { Channel = NotificationChannelType.Email, EventCode = "exam.result", IsEnabled = true });
        created.Success.Should().BeTrue(created.Message);
        created.Data!.EventCode.Should().Be("EXAM.RESULT");
        var stale = await service.SaveMyPreferenceAsync(new SaveNotificationPreferenceRequestDto
            { Channel = NotificationChannelType.Email, EventCode = "EXAM.RESULT", IsEnabled = false, RowVersion = "invalid" });
        stale.StatusCode.Should().Be(409);
    }

    private static EduOSDbContext Database()
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("communication-workspace-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }
    private static CommunicationService Service(EduOSDbContext db, bool admin) => new(db, new User(admin),
        new NoticeRepository(db), new NotificationRepository(db), db,
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
