using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace EduOS.Tests.Services;

/// <summary>
/// Explicit Windows-runner acceptance, not a scanner mock. Ordinary Linux CI never
/// counts these tests as proof that Microsoft Defender accepted/rejected actual bytes.
/// The isolated files and database contain only synthetic fixtures.
/// </summary>
public sealed class LibraryDigitalWindowsDefenderAcceptanceTests
{
    private sealed class Admin : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => 990901;
        public long UserId => 771;
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public string? FullName => null;
        public string? Email => null;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("EDUOS_WINDOWS_DEFENDER_ACCEPTANCE") == "1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_windows_defender_scans_library_publication_and_never_registers_test_malware(
        bool infected)
    {
        if (!Enabled) return; // This is not an acceptance PASS on non-Windows CI.
        Assert.True(OperatingSystem.IsWindows(), "Real Defender acceptance requires Windows.");
        var root = Path.Combine(Path.GetTempPath(), "eduos-library-defender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var admin = new Admin();
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, admin.UserId.ToString()),
                    new Claim(ClaimTypes.Role, "TenantAdmin"),
                    new Claim("TenantId", admin.TenantId.ToString())
                ], "Test"))
            };
            http.Items["TenantId"] = admin.TenantId;
            var options = new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("windows-library-scan-" + Guid.NewGuid().ToString("N")).Options;
            await using var db = new EduOSDbContext(options,
                new HttpContextAccessor { HttpContext = http });
            var book = new Book { TenantId = admin.TenantId, Title = "Defender acceptance book", IsActive = true };
            var campus = new Campus { TenantId = admin.TenantId, Name = "Acceptance campus", Code = "AC", IsActive = true };
            db.AddRange(book, campus);
            await db.SaveChangesAsync();
            var storage = new FileUploadService(Options.Create(new FileStorageSettings
            {
                BasePath = Path.Combine(root, "public"),
                PrivateBasePath = Path.Combine(root, "private"),
                MaxFileSizeMB = 5,
                // DocumentFileService invokes the mandatory real scanner regardless of this
                // legacy optional pre-upload switch (which has no scanner integration).
                EnableVirusScan = false
            }), admin, NullLogger<FileUploadService>.Instance);
            var documentFiles = new DocumentFileService(db, admin, storage,
                NullLogger<DocumentFileService>.Instance, null!);
            var library = new LibraryDigitalResourceService(db, admin, documentFiles, storage,
                TimeProvider.System, NullLogger<LibraryDigitalResourceService>.Instance);
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nSynthetic private library test\n");
            if (infected)
            {
                // Harmless EICAR standard antivirus test signature, assembled only at runtime.
                // No real malware, live tenant content or external scanner service is involved.
                var signature = "X5O!P%@AP[4" + "\\PZX54(P^)7CC)7}$" +
                    "EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
                bytes = [.. bytes, .. Encoding.ASCII.GetBytes(signature)];
            }
            var requestId = Guid.NewGuid();
            var response = await library.UploadAsync(new LibraryDigitalUploadRequestDto
            {
                RequestId = requestId, BookReference = book.PublicId,
                Title = "Scanned synthetic PDF"
            }, new PrivateFileUploadDto
            {
                FileName = "acceptance.pdf", ContentType = "application/pdf",
                Length = bytes.Length, Content = new MemoryStream(bytes)
            });
            if (infected)
            {
                Assert.False(response.Success, "Defender must never publish the EICAR test signature.");
                Assert.Empty(await db.FileAssets.ToListAsync());
                Assert.Empty(await db.Documents.ToListAsync());
                Assert.Empty(await db.AuditLogs.ToListAsync());
                var privateRoot = Path.Combine(root, "private");
                Assert.False(Directory.Exists(privateRoot) &&
                    Directory.EnumerateFiles(privateRoot, "*", SearchOption.AllDirectories).Any(),
                    "Rejected bytes must not remain in private storage.");
            }
            else
            {
                Assert.True(response.Success, response.Message);
                var asset = Assert.Single(await db.FileAssets.ToListAsync());
                Assert.True(asset.IsVerifiedSafe);
                Assert.Equal("Clean", asset.MalwareScanStateCode);
                Assert.StartsWith($"tenant-{admin.TenantId}/documents/", asset.StorageKey,
                    StringComparison.Ordinal);
                Assert.Single(await db.Documents.ToListAsync());
                Assert.Single(await db.AuditLogs.Where(x =>
                    x.Action == "LibraryDigitalResource.Publish").ToListAsync());
                var download = await library.DownloadAsync(requestId);
                Assert.True(download.Success, download.Message);
                Assert.Equal(bytes, download.Data!.Content);
                Assert.Equal("acceptance.pdf", download.Data.FileName);
                Assert.Single(await db.AuditLogs.Where(x =>
                    x.Action == "LibraryDigitalResource.Download").ToListAsync());
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
