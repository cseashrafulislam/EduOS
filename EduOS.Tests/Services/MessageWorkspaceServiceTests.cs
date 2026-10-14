using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Communication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class MessageWorkspaceServiceTests
{
    [Fact]
    public async Task Recipient_search_returns_only_active_members_of_current_tenant()
    {
        await using var db = Database();
        db.ApplicationUsers.AddRange(
            new ApplicationUser { Id = 10, FullName = "Sender" },
            new ApplicationUser { Id = 11, FullName = "Mary Active" },
            new ApplicationUser { Id = 12, FullName = "Mary Other Tenant" },
            new ApplicationUser { Id = 13, FullName = "Mary Inactive", IsActive = false });
        db.TenantMemberships.AddRange(
            new TenantMembership { TenantId = 101, UserId = 11, Status = MembershipStatus.Active },
            new TenantMembership { TenantId = 102, UserId = 12, Status = MembershipStatus.Active },
            new TenantMembership { TenantId = 101, UserId = 13, Status = MembershipStatus.Active });
        await db.SaveChangesAsync();
        var result = await Service(db).SearchRecipientsAsync("Mary");
        result.Success.Should().BeTrue(result.Message);
        result.Data!.Select(x => x.UserId).Should().Equal(11L);
    }

    [Fact]
    public async Task Thread_list_and_message_lookup_restrict_to_own_participation()
    {
        await using var db = Database();
        db.ApplicationUsers.AddRange(new ApplicationUser { Id = 10, FullName = "Me" },
            new ApplicationUser { Id = 11, FullName = "Peer" });
        var mine = new MessageThread { Id = 100, TenantId = 101, Subject = "My thread" };
        var other = new MessageThread { Id = 200, TenantId = 101, Subject = "Private" };
        db.MessageThreads.AddRange(mine, other);
        db.MessageThreadParticipants.AddRange(
            new MessageThreadParticipant { TenantId = 101, MessageThreadId = 100, UserId = 10 },
            new MessageThreadParticipant { TenantId = 101, MessageThreadId = 100, UserId = 11 },
            new MessageThreadParticipant { TenantId = 101, MessageThreadId = 200, UserId = 11 });
        db.Messages.AddRange(
            new Message { Id = 1000, TenantId = 101, MessageThreadId = 100, SenderUserId = 11, RecipientUserId = 10, Body = "Hello" },
            new Message { Id = 2000, TenantId = 101, MessageThreadId = 200, SenderUserId = 11, RecipientUserId = 12, Body = "Secret" });
        await db.SaveChangesAsync();
        var service = Service(db);
        var page = await service.GetMyThreadsAsync(1, 20);
        page.Success.Should().BeTrue(page.Message);
        page.Data!.Items.Should().ContainSingle().Which.PeerUserId.Should().Be(11);
        page.Data.Items.Single().UnreadCount.Should().Be(1);
        (await service.GetThreadForMessageAsync(2000)).StatusCode.Should().Be(404);
        (await service.GetThreadForMessageAsync(1000)).Data!.Reference.Should().Be(mine.PublicId);
    }

    [Fact]
    public void Messaging_api_has_authentication_and_antiforgery()
    {
        var type = typeof(CommunicationWorkspaceApiController);
        Attribute.IsDefined(type, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)).Should().BeTrue();
        Attribute.IsDefined(type, typeof(AutoValidateAntiforgeryTokenAttribute)).Should().BeTrue();
    }

    private static EduOSDbContext Database()
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("message-workspace-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }
    private static MessageWorkspaceService Service(EduOSDbContext db) => new(db, new User(), db);
    private sealed class User : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 10;
        public long TenantId => 101;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => false;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
