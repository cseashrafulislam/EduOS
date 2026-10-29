using System.Security.Cryptography;
using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Communication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class MessageAttachmentWorkflowTests
{
    [Fact]
    public async Task Listing_and_download_are_tenant_scoped_and_require_active_participation()
    {
        await using var db = Database();
        db.TenantMemberships.Add(new TenantMembership { TenantId = 101, UserId = 10, Status = MembershipStatus.Active });
        db.MessageThreads.Add(new MessageThread { Id = 100, TenantId = 101 });
        db.MessageThreadParticipants.Add(new MessageThreadParticipant { TenantId = 101, MessageThreadId = 100, UserId = 10 });
        db.Messages.Add(new Message { Id = 1000, TenantId = 101, MessageThreadId = 100, SenderUserId = 10, RecipientUserId = 11, Body = "Hi" });
        db.Messages.Add(new Message { Id = 2000, TenantId = 202, MessageThreadId = 200, SenderUserId = 10, RecipientUserId = 11, Body = "Foreign" });
        var bytes = new byte[] { 37, 80, 68, 70, 45 };
        var reference = Guid.NewGuid();
        db.FileAssets.Add(new FileAsset { Id = 1, TenantId = 101, PublicId = reference, StorageProvider = "LocalPrivate",
            StorageKey = "tenant-101/message-attachments/2026/10/one.pdf", OriginalFileName = "one.pdf",
            ContentType = "application/pdf", SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            IsVerifiedSafe = true, MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private });
        db.MessageAttachments.Add(new MessageAttachment { TenantId = 101, MessageId = 1000, FileAssetId = 1 });
        await TestTenantSeeder.SaveAsync(db);
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(It.IsAny<string>())).ReturnsAsync(new FileDownloadResult { Content = bytes });
        var service = Service(db, storage);
        (await service.GetForMessageAsync(2000)).StatusCode.Should().Be(404);
        var list = await service.GetForMessageAsync(1000);
        list.Success.Should().BeTrue(list.Message);
        list.Data!.Items.Should().ContainSingle().Which.FileName.Should().Be("one.pdf");
        System.Text.Json.JsonSerializer.Serialize(list.Data).Should().NotContain("StorageKey");
        (await service.DownloadAsync(1000, reference)).Success.Should().BeTrue();
        storage.Setup(x => x.GetPrivateFileAsync(It.IsAny<string>())).ReturnsAsync(new FileDownloadResult { Content = new byte[] { 1, 2, 3, 4, 5 } });
        (await service.DownloadAsync(1000, reference)).StatusCode.Should().Be(409);
        db.MessageThreadParticipants.RemoveRange(db.MessageThreadParticipants);
        await db.SaveChangesAsync();
        (await service.DownloadAsync(1000, reference)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Upload_rejects_inaccessible_message_without_accessing_storage()
    {
        await using var db = Database();
        var storage = new Mock<IFileUploadService>(MockBehavior.Strict);
        var service = Service(db, storage);
        await using var input = new MemoryStream(new byte[] { 37, 80, 68, 70, 45 });
        var result = await service.UploadAsync(999, Guid.NewGuid(), new EduOS.Core.DTOs.Files.PrivateFileUploadDto
        { FileName = "a.pdf", ContentType = "application/pdf", Length = input.Length, Content = input });
        result.StatusCode.Should().Be(404);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_rejects_storage_quota_before_accessing_private_storage()
    {
        await using var db = Database();
        db.TenantMemberships.AddRange(
            new TenantMembership { TenantId = 101, UserId = 10, Status = MembershipStatus.Active },
            new TenantMembership { TenantId = 101, UserId = 11, Status = MembershipStatus.Active });
        db.MessageThreads.Add(new MessageThread { Id = 100, TenantId = 101 });
        db.MessageThreadParticipants.Add(new MessageThreadParticipant
            { TenantId = 101, MessageThreadId = 100, UserId = 10 });
        db.Messages.Add(new Message { Id = 1000, TenantId = 101, MessageThreadId = 100,
            SenderUserId = 10, RecipientUserId = 11, Body = "Quota" });
        var plan = new SubscriptionPlan { Code = "MESSAGE-CAP", Name = "Message cap", MaxStorageMb = 1 };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = 101, SubscriptionPlanId = plan.Id, State = SubscriptionState.Active,
            StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(1)
        });
        db.FileAssets.Add(new FileAsset
        {
            TenantId = 101, StorageProvider = "LocalPrivate",
            StorageKey = "tenant-101/documents/previous.pdf",
            OriginalFileName = "previous.pdf", ContentType = "application/pdf", SizeBytes = 1048576
        });
        await db.SaveChangesAsync();

        var storage = new Mock<IFileUploadService>(MockBehavior.Strict);
        var service = Service(db, storage);
        await using var input = new MemoryStream(new byte[] { 37, 80, 68, 70, 45 });
        var response = await service.UploadAsync(1000, Guid.NewGuid(), new EduOS.Core.DTOs.Files.PrivateFileUploadDto
        { FileName = "quota.pdf", ContentType = "application/pdf", Length = input.Length, Content = input });

        response.StatusCode.Should().Be(409);
        db.MessageAttachments.Should().BeEmpty();
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Scanner_outage_fails_closed_and_cleans_private_file()
    {
        await using var db = Database();
        db.TenantMemberships.AddRange(
            new TenantMembership { TenantId = 101, UserId = 10, Status = MembershipStatus.Active },
            new TenantMembership { TenantId = 101, UserId = 11, Status = MembershipStatus.Active });
        db.MessageThreads.Add(new MessageThread { Id = 100, TenantId = 101 });
        db.MessageThreadParticipants.Add(new MessageThreadParticipant { TenantId = 101, MessageThreadId = 100, UserId = 10 });
        db.Messages.Add(new Message { Id = 1000, TenantId = 101, MessageThreadId = 100,
            SenderUserId = 10, RecipientUserId = 11, Body = "Hello" });
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.ValidateFile(It.IsAny<IFormFile>())).Returns(true);
        storage.Setup(x => x.UploadPrivateAsync(It.IsAny<IFormFile>(), "message-attachments"))
            .ReturnsAsync(new FileUploadResult { Success = true,
                FileUrl = "tenant-101/message-attachments/2026/10/new.pdf" });
        storage.Setup(x => x.ScanPrivateForMalwareAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        storage.Setup(x => x.DeletePrivateAsync(It.IsAny<string>())).ReturnsAsync(true);
        var service = Service(db, storage);
        await using var input = new MemoryStream(new byte[] { 37, 80, 68, 70, 45 });
        var result = await service.UploadAsync(1000, Guid.NewGuid(), new EduOS.Core.DTOs.Files.PrivateFileUploadDto
        { FileName = "a.pdf", ContentType = "application/pdf", Length = input.Length, Content = input });
        result.StatusCode.Should().Be(503);
        db.FileAssets.Should().BeEmpty();
        db.MessageAttachments.Should().BeEmpty();
        storage.Verify(x => x.DeletePrivateAsync("tenant-101/message-attachments/2026/10/new.pdf"), Times.Once);
    }

    [Fact]
    public void Api_requires_authentication_and_antiforgery()
    {
        var type = typeof(MessageAttachmentsApiController);
        Attribute.IsDefined(type, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)).Should().BeTrue();
        Attribute.IsDefined(type, typeof(AutoValidateAntiforgeryTokenAttribute)).Should().BeTrue();
    }

    private static EduOSDbContext Database()
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("message-attachments-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }
    private static MessageAttachmentService Service(EduOSDbContext db, Mock<IFileUploadService> storage) =>
        new(db, new User(), storage.Object, db, NullLogger<MessageAttachmentService>.Instance);
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
