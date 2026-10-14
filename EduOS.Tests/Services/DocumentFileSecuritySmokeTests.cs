using EduOS.Core.Common;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DocumentFileSecuritySmokeTests
{
    private static EduOSDbContext Context() => new(new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase("document-security-" + Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public async Task NonAdminCannotDownload()
    {
        await using var db = Context();
        var service = new DocumentFileService(db, new User(false), null!, NullLogger<DocumentFileService>.Instance, null!);
        Assert.Equal(403, (await service.DownloadAsync(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task NonAdminCannotUpload()
    {
        await using var db = Context();
        var service = new DocumentFileService(db, new User(false), null!, NullLogger<DocumentFileService>.Instance, null!);
        Assert.Equal(403, (await service.UploadAsync(Guid.NewGuid(), null!)).StatusCode);
    }

    [Theory]
    [InlineData("evil.exe", "application/octet-stream")]
    [InlineData("report.pdf", "text/html")]
    [InlineData("script.html", "text/html")]
    public async Task UnsupportedFormatsRejectedBeforeStorage(string fileName, string contentType)
    {
        await using var db = Context();
        var service = new DocumentFileService(db, new User(true), null!, NullLogger<DocumentFileService>.Instance, null!);
        using var stream = new MemoryStream(new byte[]{1,2,3});
        var request = new PrivateFileUploadDto { FileName=fileName, ContentType=contentType, Length=3, Content=stream };
        Assert.Equal(400, (await service.UploadAsync(Guid.NewGuid(), request)).StatusCode);
        Assert.Empty(db.FileAssets);
    }


    [Theory]
    [InlineData(42L, 1, 404)]
    [InlineData(99L, 0, 404)]
    public async Task ProtectedDownloadDelegatesOnlyWithinOwnTenant(long assetTenantId, int expectedCalls, int expectedStatus)
    {
        await using var db = Context();
        var fileReference = Guid.NewGuid();
        var documentReference = Guid.NewGuid();
        db.FileAssets.Add(new FileAsset
        {
            Id = 101, TenantId = assetTenantId, PublicId = fileReference,
            StorageProvider = "LocalPrivate", StorageKey = "tenant-" + assetTenantId + "/documents/test.pdf",
            OriginalFileName = "test.pdf", ContentType = "application/pdf", SizeBytes = 4,
            Sha256 = "DEADBEEF", Visibility = FileVisibility.Private,
            IsVerifiedSafe = true, MalwareScanStateCode = "Clean"
        });
        db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
        {
            Id = 102, TenantId = assetTenantId, Code = "SENSITIVE",
            Name = "Sensitive", IsActive = true, IsSensitive = true
        });
        db.Documents.Add(new Document
        {
            Id = 103, TenantId = assetTenantId, FileAssetId = 101,
            PublicId = documentReference, DocumentTypeCode = "SENSITIVE",
            IsActive = true, VerificationStatusCode = "Approved"
        });
        await db.SaveChangesAsync();
        var verification = new VerificationStub();
        var service = new DocumentFileService(db, new User(true), null!,
            NullLogger<DocumentFileService>.Instance, verification);
        var result = await service.DownloadAsync(fileReference);
        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(expectedCalls, verification.Calls);
        if (expectedCalls == 1) Assert.Equal(documentReference, verification.LastReference);
    }

    [Fact]
    public async Task DuplicateProtectedFileAttachmentsFailClosedBeforeDelegation()
    {
        await using var db = Context();
        var fileReference = Guid.NewGuid();
        db.FileAssets.Add(new FileAsset
        {
            Id = 201, TenantId = 42, PublicId = fileReference,
            StorageProvider = "LocalPrivate", StorageKey = "tenant-42/documents/test.pdf",
            OriginalFileName = "test.pdf", ContentType = "application/pdf", SizeBytes = 4,
            Sha256 = "DEADBEEF", Visibility = FileVisibility.Private,
            IsVerifiedSafe = true, MalwareScanStateCode = "Clean"
        });
        db.Documents.AddRange(
            new Document { Id = 202, TenantId = 42, FileAssetId = 201, DocumentTypeCode = "SENSITIVE" },
            new Document { Id = 203, TenantId = 42, FileAssetId = 201, DocumentTypeCode = "SENSITIVE" });
        await db.SaveChangesAsync();
        var verification = new VerificationStub();
        var service = new DocumentFileService(db, new User(true), null!,
            NullLogger<DocumentFileService>.Instance, verification);
        Assert.Equal(404, (await service.DownloadAsync(fileReference)).StatusCode);
        Assert.Equal(0, verification.Calls);
    }

    private sealed class VerificationStub : IDocumentVerificationService
    {
        public int Calls { get; private set; }
        public Guid LastReference { get; private set; }
        public Task<ApiResponse<DocumentFileDownloadDto>> DownloadApprovedAsync(Guid reference, CancellationToken ct = default)
        {
            Calls++;
            LastReference = reference;
            return Task.FromResult(ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Approval required.", 404));
        }
        public Task<ApiResponse<DocumentFileDownloadDto>> DownloadForReviewAsync(Guid reference, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ApiResponse<PagedResult<DocumentVerificationDto>>> GetReviewQueueAsync(string? status, int page, int pageSize, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ApiResponse<DocumentVerificationDto>> ReviewAsync(Guid reference, DocumentVerificationDecisionDto request, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class User(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => 42;
        public long UserId => 77;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Teacher"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}