using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Disposable SQL Server V1 Student/Campus -> V2 Library private-document boundary.
/// The existing Transport/Hostel fixture supplies real tenants, students, enrollments
/// and authenticated scoped DbContexts. No fake second learner identity is created.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed class DigitalReader(long tenant, long user, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => role == name;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static LibraryDigitalResourceService DigitalService(
        EduOS.Persistence.Context.EduOSDbContext db, Fixture fixture,
        string role, Mock<IFileUploadService> storage) => new(
            db, new DigitalReader(fixture.TenantId, fixture.UserId, role),
            new Mock<IDocumentFileService>().Object, storage.Object,
            TimeProvider.System, NullLogger<LibraryDigitalResourceService>.Instance);

    private static async Task<(Guid Reference, string Key, byte[] Bytes)> SeedDigitalResourceAsync(
        Fixture fixture)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var enrollment = await db.StudentEnrollments.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
        var student = await db.Students.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.Id == enrollment.StudentId);
        student.UserId = fixture.UserId;
        var reference = Guid.NewGuid();
        var key = $"tenant-{fixture.TenantId}/documents/{reference:N}.pdf";
        var bytes = "%PDF-1.4\nPrivate library integration fixture"u8.ToArray();
        var book = new Book
        {
            TenantId = fixture.TenantId, Title = "Private V2 library evidence", IsActive = true
        };
        var type = new DocumentTypeDefinition
        {
            TenantId = fixture.TenantId, Code = "LIBRARY_DIGITAL",
            Name = "Digital library", IsActive = true
        };
        var asset = new FileAsset
        {
            TenantId = fixture.TenantId, PublicId = reference,
            StorageProvider = "LocalPrivate", StorageKey = key,
            OriginalFileName = "evidence.pdf", ContentType = "application/pdf",
            SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            UploadedByUserId = fixture.UserId, IsVerifiedSafe = true,
            MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private
        };
        db.AddRange(book, type, asset);
        await db.SaveChangesAsync();
        db.Documents.Add(new Document
        {
            TenantId = fixture.TenantId, PublicId = reference,
            FileAssetId = asset.Id, DocumentTypeCode = "LIBRARY_DIGITAL",
            EntityType = "LibraryBook", EntityId = book.Id,
            Title = "Private evidence", IsActive = true
        });
        await db.SaveChangesAsync();
        return (reference, key, bytes);
    }

    private static Mock<IFileUploadService> DigitalStorage(
        (Guid Reference, string Key, byte[] Bytes) asset)
    {
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(asset.Key))
            .ReturnsAsync(new FileDownloadResult
            {
                Content = asset.Bytes, ContentType = "application/pdf"
            });
        return storage;
    }

    [Fact]
    public async Task V2_library_sql_direct_id_probe_cannot_read_other_institutes_private_file()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var first = await SeedAsync(connection);
        var second = await SeedAsync(connection);
        var own = await SeedDigitalResourceAsync(first);
        var foreign = await SeedDigitalResourceAsync(second);
        var storage = DigitalStorage(own);
        // Each tenant gets its own ambient HttpContext; keep their scopes
        // sequential so the AsyncLocal test accessor cannot switch the first.
        await using (var db = ScopedContext(first.Options, first.TenantId, first.UserId))
        {
            var reader = DigitalService(db, first, "Student", storage);
            var list = await reader.ListAsync(null, null, 1, 20, false);
            Assert.True(list.Success, list.Message);
            Assert.Equal(own.Reference, Assert.Single(list.Data!.Items).Reference);
            Assert.Equal(404, (await reader.DownloadAsync(foreign.Reference)).StatusCode);
            storage.Verify(x => x.GetPrivateFileAsync(foreign.Key), Times.Never);
            var permitted = await reader.DownloadAsync(own.Reference);
            Assert.True(permitted.Success, permitted.Message);
            Assert.Equal(own.Bytes, permitted.Data!.Content);
            Assert.Equal(1, await db.AuditLogs.CountAsync(x =>
                x.TenantId == first.TenantId && x.Action == "LibraryDigitalResource.Download"));
        }

        // Same SQL database, independent institute and tenant context.
        await using (var otherDb = ScopedContext(second.Options, second.TenantId, second.UserId))
        {
            var otherStorage = DigitalStorage(foreign);
            var otherReader = DigitalService(otherDb, second, "Student", otherStorage);
            Assert.Equal(404, (await otherReader.DownloadAsync(own.Reference)).StatusCode);
            Assert.Equal(foreign.Reference,
                Assert.Single((await otherReader.ListAsync(null, null, 1, 20, false)).Data!.Items).Reference);
            Assert.True((await otherReader.DownloadAsync(foreign.Reference)).Success);
            Assert.Equal(1, await otherDb.AuditLogs.CountAsync(x =>
                x.TenantId == second.TenantId && x.Action == "LibraryDigitalResource.Download"));
        }
    }

    [Fact]
    public async Task V1_campus_and_enrollment_revocation_immediately_remove_V2_library_access_on_sql()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var asset = await SeedDigitalResourceAsync(fixture);
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var reader = DigitalService(db, fixture, "Student", DigitalStorage(asset));
        Assert.True((await reader.DownloadAsync(asset.Reference)).Success);

        await using (var admin = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await admin.StudentEnrollments.SingleAsync(x =>
                x.PublicId == fixture.Enrollments[0]);
            var campus = await admin.Campuses.SingleAsync(x => x.Id == enrollment.CampusId);
            campus.IsActive = false;
            await admin.SaveChangesAsync();
        }
        Assert.Equal(403, (await reader.ListAsync(null, null, 1, 20, false)).StatusCode);
        Assert.Equal(404, (await reader.DownloadAsync(asset.Reference)).StatusCode);

        await using (var admin = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await admin.StudentEnrollments.SingleAsync(x =>
                x.PublicId == fixture.Enrollments[0]);
            var campus = await admin.Campuses.SingleAsync(x => x.Id == enrollment.CampusId);
            campus.IsActive = true;
            enrollment.IsCurrent = false;
            enrollment.State = EnrollmentState.Withdrawn;
            await admin.SaveChangesAsync();
        }
        Assert.Equal(404, (await reader.DownloadAsync(asset.Reference)).StatusCode);
        Assert.Equal(403, (await reader.ListAsync(null, null, 1, 20, false)).StatusCode);

        await using (var admin = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await admin.StudentEnrollments.SingleAsync(x =>
                x.PublicId == fixture.Enrollments[0]);
            enrollment.IsCurrent = true;
            enrollment.State = EnrollmentState.Active;
            var student = await admin.Students.SingleAsync(x => x.Id == enrollment.StudentId);
            student.StatusCode = "Transferred";
            await admin.SaveChangesAsync();
        }
        Assert.Equal(404, (await reader.DownloadAsync(asset.Reference)).StatusCode);
        await using (var admin = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var enrollment = await admin.StudentEnrollments.SingleAsync(x =>
                x.PublicId == fixture.Enrollments[0]);
            var student = await admin.Students.SingleAsync(x => x.Id == enrollment.StudentId);
            student.StatusCode = "Active";
            await admin.SaveChangesAsync();
        }
        Assert.True((await reader.DownloadAsync(asset.Reference)).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V2_library_sql_download_rechecks_V1_membership_and_resource_after_storage_io(
        bool revokeDocument)
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var asset = await SeedDigitalResourceAsync(fixture);
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(asset.Key)).Returns(async () =>
        {
            // This separate SQL connection commits a revocation while the
            // original authorized request is waiting for private storage.
            await using var admin = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            if (revokeDocument)
            {
                var document = await admin.Documents.SingleAsync(x =>
                    x.PublicId == asset.Reference);
                document.IsActive = false;
            }
            else
            {
                var enrollment = await admin.StudentEnrollments.SingleAsync(x =>
                    x.PublicId == fixture.Enrollments[0]);
                enrollment.IsCurrent = false;
                enrollment.State = EnrollmentState.Withdrawn;
            }
            await admin.SaveChangesAsync();
            return new FileDownloadResult { Content = asset.Bytes, ContentType = "application/pdf" };
        });
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var reader = DigitalService(db, fixture, "Student", storage);
        var denied = await reader.DownloadAsync(asset.Reference);
        Assert.Equal(404, denied.StatusCode);
        Assert.False(denied.Success);
        Assert.Equal(0, await db.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.Action == "LibraryDigitalResource.Download"));
        storage.Verify(x => x.GetPrivateFileAsync(asset.Key), Times.Once);
    }
}
