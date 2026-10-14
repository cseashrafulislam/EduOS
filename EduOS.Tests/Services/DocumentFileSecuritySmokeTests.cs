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
        var service = new DocumentFileService(db, new User(false), null!, NullLogger<DocumentFileService>.Instance);
        Assert.Equal(403, (await service.DownloadAsync(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task NonAdminCannotUpload()
    {
        await using var db = Context();
        var service = new DocumentFileService(db, new User(false), null!, NullLogger<DocumentFileService>.Instance);
        Assert.Equal(403, (await service.UploadAsync(Guid.NewGuid(), null!)).StatusCode);
    }

    [Theory]
    [InlineData("evil.exe", "application/octet-stream")]
    [InlineData("report.pdf", "text/html")]
    [InlineData("script.html", "text/html")]
    public async Task UnsupportedFormatsRejectedBeforeStorage(string fileName, string contentType)
    {
        await using var db = Context();
        var service = new DocumentFileService(db, new User(true), null!, NullLogger<DocumentFileService>.Instance);
        using var stream = new MemoryStream(new byte[]{1,2,3});
        var request = new PrivateFileUploadDto { FileName=fileName, ContentType=contentType, Length=3, Content=stream };
        Assert.Equal(400, (await service.UploadAsync(Guid.NewGuid(), request)).StatusCode);
        Assert.Empty(db.FileAssets);
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