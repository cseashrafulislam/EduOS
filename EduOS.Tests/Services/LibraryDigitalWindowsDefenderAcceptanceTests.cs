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
using System.IO.Compression;
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
            var fileName = infected ? "acceptance.docx" : "acceptance.pdf";
            var contentType = infected
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                : "application/pdf";
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nSynthetic private library test\n");
            if (infected)
            {
                // A genuine ZIP-based DOCX containing a standalone EICAR test entry.
                // Defender can inspect the archived test file; appending EICAR after a
                // PDF trailer is not a reliable standard antivirus test fixture.
                var signature = "X5O!P%@AP[4" + "\\PZX54(P^)7CC)7}$" +
                    "EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
                using var zipStream = new MemoryStream();
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    static void AddText(ZipArchive archive, string path, string content)
                    {
                        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                        writer.Write(content);
                    }
                    AddText(archive, "[Content_Types].xml",
                        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                        "</Types>");
                    AddText(archive, "_rels/.rels",
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                        "</Relationships>");
                    AddText(archive, "word/document.xml",
                        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/></w:body></w:document>");
                    AddText(archive, "word/embeddings/eicar.com", signature);
                }
                bytes = zipStream.ToArray();
            }
            var requestId = Guid.NewGuid();
            var response = await library.UploadAsync(new LibraryDigitalUploadRequestDto
            {
                RequestId = requestId, BookReference = book.PublicId,
                Title = "Scanned synthetic PDF"
            }, new PrivateFileUploadDto
            {
                FileName = fileName, ContentType = contentType,
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
