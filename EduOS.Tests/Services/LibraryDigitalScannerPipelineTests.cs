using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using System.Security.Cryptography;
using Xunit;

namespace EduOS.Tests.Services;

// Real DocumentFileService -> LibraryDigitalResourceService pipeline with an
// isolated private-storage scanner double. No production file or live scanner used.
public sealed class LibraryDigitalScannerPipelineTests
{
    private sealed class Admin : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => 901;
        public long UserId => 17;
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public string? FullName => null;
        public string? Email => null;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static EduOSDbContext Context()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "17"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", "901")
            ], "Test"))
        };
        http.Items["TenantId"] = 901L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("library-scanner-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }

    [Theory]
    [InlineData("clean", 200)]
    [InlineData("infected", 503)]
    [InlineData("unavailable", 503)]
    [InlineData("exception", 503)]
    public async Task Scanned_library_publication_fails_closed_and_cleans_rejected_private_bytes(
        string scannerState, int expectedStatus)
    {
        await using var db = Context();
        var admin = new Admin();
        var book = new Book { TenantId = admin.TenantId, Title = "Secure digital publication", IsActive = true };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var requestId = Guid.NewGuid();
        var bytes = "%PDF-1.4\nScanned library upload"u8.ToArray();
        var key = $"tenant-{admin.TenantId}/documents/{requestId:N}.pdf";
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.UploadPrivateAsync(It.IsAny<IFormFile>(), "documents"))
            .ReturnsAsync(new FileUploadResult { Success = true, FileUrl = key });
        storage.Setup(x => x.DeletePrivateAsync(key)).ReturnsAsync(true);
        if (scannerState == "exception")
            storage.Setup(x => x.ScanPrivateForMalwareAsync(key, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Scanner process unavailable"));
        else
            storage.Setup(x => x.ScanPrivateForMalwareAsync(key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(scannerState == "clean" ? sha :
                    scannerState == "infected" ? "INFECTED" : null);
        var documentFiles = new DocumentFileService(db, admin, storage.Object,
            NullLogger<DocumentFileService>.Instance, null!);
        var digital = new LibraryDigitalResourceService(db, admin, documentFiles,
            storage.Object, TimeProvider.System, NullLogger<LibraryDigitalResourceService>.Instance);
        var request = new LibraryDigitalUploadRequestDto
        {
            RequestId = requestId, BookReference = book.PublicId,
            Title = "Licensed PDF"
        };
        var response = await digital.UploadAsync(request, new PrivateFileUploadDto
        {
            FileName = "licensed.pdf", ContentType = "application/pdf",
            Length = bytes.Length, Content = new MemoryStream(bytes)
        });
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(scannerState == "clean", response.Success);
        Assert.Equal(scannerState == "clean" ? 1 : 0, await db.FileAssets.CountAsync());
        Assert.Equal(scannerState == "clean" ? 1 : 0, await db.Documents.CountAsync());
        Assert.Equal(scannerState == "clean" ? 1 : 0, await db.AuditLogs.CountAsync(x =>
            x.Action == "LibraryDigitalResource.Publish"));
        if (scannerState == "clean")
        {
            storage.Verify(x => x.DeletePrivateAsync(key), Times.Never);
            var replay = await digital.UploadAsync(request, new PrivateFileUploadDto
            {
                FileName = "licensed.pdf", ContentType = "application/pdf",
                Length = bytes.Length, Content = new MemoryStream(bytes)
            });
            Assert.True(replay.Success, replay.Message);
            Assert.Single(await db.Documents.ToListAsync());
            Assert.Single(await db.FileAssets.ToListAsync());
        }
        else
        {
            storage.Verify(x => x.DeletePrivateAsync(key), Times.Once);
            storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Never);
        }
    }

    [Fact]
    public async Task Interrupted_scanner_cleans_private_bytes_and_same_request_can_recover_without_orphan()
    {
        await using var db = Context();
        var admin = new Admin();
        var book = new Book { TenantId = admin.TenantId, Title = "Interrupted upload", IsActive = true };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var requestId = Guid.NewGuid();
        var bytes = "%PDF-1.4\\nRecoverable upload"u8.ToArray();
        var key = $"tenant-{admin.TenantId}/documents/{requestId:N}.pdf";
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.UploadPrivateAsync(It.IsAny<IFormFile>(), "documents"))
            .ReturnsAsync(new FileUploadResult { Success = true, FileUrl = key });
        storage.Setup(x => x.DeletePrivateAsync(key)).ReturnsAsync(true);
        var scanCount = 0;
        storage.Setup(x => x.ScanPrivateForMalwareAsync(key, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++scanCount == 1)
                    throw new OperationCanceledException("Interrupted security scan");
                return Task.FromResult<string?>(sha);
            });
        var documents = new DocumentFileService(db, admin, storage.Object,
            NullLogger<DocumentFileService>.Instance, null!);
        var library = new LibraryDigitalResourceService(db, admin, documents,
            storage.Object, TimeProvider.System, NullLogger<LibraryDigitalResourceService>.Instance);
        var request = new LibraryDigitalUploadRequestDto
        { RequestId = requestId, BookReference = book.PublicId, Title = "Recoverable PDF" };
        PrivateFileUploadDto Upload() => new()
        {
            FileName = "recovery.pdf", ContentType = "application/pdf",
            Length = bytes.Length, Content = new MemoryStream(bytes)
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.UploadAsync(request, Upload()));
        Assert.Empty(await db.FileAssets.ToListAsync());
        Assert.Empty(await db.Documents.ToListAsync());
        storage.Verify(x => x.DeletePrivateAsync(key), Times.Once);

        var recovered = await library.UploadAsync(request, Upload());
        Assert.True(recovered.Success, recovered.Message);
        Assert.Single(await db.FileAssets.ToListAsync());
        Assert.Single(await db.Documents.ToListAsync());
        Assert.Single(await db.AuditLogs.Where(x =>
            x.Action == "LibraryDigitalResource.Publish").ToListAsync());
        storage.Verify(x => x.DeletePrivateAsync(key), Times.Once);
        Assert.Equal(2, scanCount);
    }

}
