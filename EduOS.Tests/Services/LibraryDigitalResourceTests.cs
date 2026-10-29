using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Library;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using System.Security.Cryptography;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryDigitalResourceTests
{
    private static EduOSDbContext Context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("library-digital-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "TenantAdmin"), new Claim("TenantId", "101")
        ], "Test")) };
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static LibraryDigitalResourceService Service(EduOSDbContext db, string role,
        Mock<IDocumentFileService>? documents = null, Mock<IFileUploadService>? storage = null) =>
        new(db, new User(role), (documents ?? new Mock<IDocumentFileService>()).Object,
            (storage ?? new Mock<IFileUploadService>()).Object,
            TimeProvider.System, NullLogger<LibraryDigitalResourceService>.Instance);

    private static async Task<(Book Book, StudentEnrollment Enrollment, FileAsset Asset, byte[] Bytes)> SeedAsync(
        EduOSDbContext db, Guid reference)
    {
        var bytes = "%PDF-1.4\nTest"u8.ToArray();
        var campus = new Campus { TenantId = 101, Name = "A", Code = "A", IsActive = true };
        var book = new Book { TenantId = 101, Title = "Digital title", IsActive = true };
        var student = new Student { TenantId = 101, PersonId = 11, StudentCode = "A-1",
            FullName = "Reader", UserId = 7, StatusCode = "Active" };
        db.AddRange(campus, book, student);
        await db.SaveChangesAsync();
        var enrollment = new StudentEnrollment { TenantId = 101, StudentId = student.Id,
            CampusId = campus.Id, State = EnrollmentState.Active, IsCurrent = true,
            RollNo = "1", EnrollmentDate = new DateOnly(2026, 1, 1) };
        var asset = new FileAsset
        {
            TenantId = 101, PublicId = reference, StorageProvider = "LocalPrivate",
            StorageKey = "tenant-101/documents/verified.pdf", OriginalFileName = "verified.pdf",
            ContentType = "application/pdf", SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), UploadedByUserId = 7,
            MalwareScanStateCode = "Clean", IsVerifiedSafe = true,
            Visibility = FileVisibility.Private
        };
        db.AddRange(enrollment, asset);
        await db.SaveChangesAsync();
        return (book, enrollment, asset, bytes);
    }

    [Fact]
    public async Task Scanned_resource_requires_live_student_enrollment_for_list_and_download()
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        var seed = await SeedAsync(db, reference);
        db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
        {
            TenantId = 101, Code = "LIBRARY_DIGITAL", Name = "Digital",
            IsActive = true
        });
        db.Documents.Add(new Document
        {
            TenantId = 101, PublicId = reference, FileAssetId = seed.Asset.Id,
            DocumentTypeCode = "LIBRARY_DIGITAL", EntityType = "LibraryBook",
            EntityId = seed.Book.Id, Title = "Reference PDF", IsActive = true
        });
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(seed.Asset.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = seed.Bytes, ContentType = "application/pdf" });
        var student = Service(db, "Student", storage: storage);
        var list = await student.ListAsync(null, null, 1, 20, false);
        list.Success.Should().BeTrue();
        list.Data!.Items.Should().ContainSingle();
        (await student.DownloadAsync(reference)).Success.Should().BeTrue();
        (await db.AuditLogs.AnyAsync(x => x.Action == "LibraryDigitalResource.Download")).Should().BeTrue();
        seed.Enrollment.IsCurrent = false;
        await db.SaveChangesAsync();
        (await student.ListAsync(null, null, 1, 20, false)).StatusCode.Should().Be(403);
        (await student.DownloadAsync(reference)).StatusCode.Should().Be(404);
        (await Service(db, "Principal").ListAsync(null, null, 1, 20, false))
            .StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Upload_is_idempotent_and_revocation_keeps_audit_history()
    {
        await using var db = Context();
        var requestId = Guid.NewGuid();
        var seed = await SeedAsync(db, requestId);
        var files = new Mock<IDocumentFileService>();
        files.Setup(x => x.UploadAsync(requestId, It.IsAny<PrivateFileUploadDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<FileAssetDto>.SuccessResponse(new FileAssetDto
            { Id = seed.Asset.Id, Reference = requestId, IsVerifiedSafe = true }));
        var service = Service(db, "TenantAdmin", documents: files);
        var request = new LibraryDigitalUploadRequestDto
        { RequestId = requestId, BookReference = seed.Book.PublicId, Title = "Reference PDF",
          ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) };
        PrivateFileUploadDto Upload() => new()
        {
            FileName = "verified.pdf", ContentType = "application/pdf",
            Length = seed.Bytes.Length, Content = new MemoryStream(seed.Bytes)
        };
        var first = await service.UploadAsync(request, Upload());
        first.Success.Should().BeTrue(first.Message);
        first.Data!.ExpiresOn.Should().Be(request.ExpiresOn);
        var replay = await service.UploadAsync(request, Upload());
        replay.Success.Should().BeTrue(replay.Message);
        (await db.Documents.CountAsync(x => x.DocumentTypeCode == "LIBRARY_DIGITAL")).Should().Be(1);
        (await service.UploadAsync(new LibraryDigitalUploadRequestDto
        { RequestId = requestId, BookReference = seed.Book.PublicId, Title = "Reference PDF",
          ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(11)) }, Upload()))
            .StatusCode.Should().Be(409);
        var row = await db.Documents.SingleAsync(x => x.PublicId == requestId);
        row.ExpiresAt.Should().HaveValue();
        DateOnly.FromDateTime(row.ExpiresAt!.Value).Should().Be(request.ExpiresOn);
        row.RowVersion = [1, 2, 3];
        await db.SaveChangesAsync();
        var revoked = await service.RevokeAsync(requestId,
            new LibraryDigitalRevokeRequestDto { RowVersion = Convert.ToBase64String(row.RowVersion) });
        revoked.Success.Should().BeTrue(revoked.Message);
        (await db.Documents.SingleAsync(x => x.PublicId == requestId)).IsActive.Should().BeFalse();
        (await db.AuditLogs.CountAsync(x => x.EntityName == "LibraryDigitalResource")).Should().Be(2);
        (await service.DownloadAsync(requestId)).StatusCode.Should().Be(404);
        (await service.UploadAsync(new LibraryDigitalUploadRequestDto
        { RequestId = requestId, BookReference = seed.Book.PublicId, Title = "Different" }, Upload()))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Principal_requires_active_campus_grant_and_modified_private_file_is_denied()
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        var seed = await SeedAsync(db, reference);
        var campusId = seed.Enrollment.CampusId;
        db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
        {
            TenantId = 101, Code = "LIBRARY_DIGITAL", Name = "Digital", IsActive = true
        });
        db.Documents.Add(new Document
        {
            TenantId = 101, PublicId = reference, FileAssetId = seed.Asset.Id,
            DocumentTypeCode = "LIBRARY_DIGITAL", EntityType = "LibraryBook",
            EntityId = seed.Book.Id, Title = "Reference", IsActive = true
        });
        var grant = new UserCampusAccess
        { TenantId = 101, UserId = 7, CampusId = campusId, IsActive = true };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(seed.Asset.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = "tampered"u8.ToArray() });
        var principal = Service(db, "Principal", storage: storage);
        (await principal.ListAsync(null, null, 1, 20, false)).Success.Should().BeTrue();
        (await principal.DownloadAsync(reference)).StatusCode.Should().Be(409);
        (await db.AuditLogs.AnyAsync(x => x.Action == "LibraryDigitalResource.Download")).Should().BeFalse();
        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.ListAsync(null, null, 1, 20, false)).StatusCode.Should().Be(403);
        (await principal.DownloadAsync(reference)).StatusCode.Should().Be(404);
    }
    [Fact]
    public async Task Expiry_extension_requires_admin_and_current_version_and_preserves_audit()
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        var seed = await SeedAsync(db, reference);
        db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
        { TenantId = 101, Code = "LIBRARY_DIGITAL", Name = "Digital", IsActive = true });
        var document = new Document
        {
            TenantId = 101, PublicId = reference, FileAssetId = seed.Asset.Id,
            DocumentTypeCode = "LIBRARY_DIGITAL", EntityType = "LibraryBook",
            EntityId = seed.Book.Id, Title = "Old", IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-2), RowVersion = [1, 2, 3]
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(seed.Asset.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = seed.Bytes, ContentType = "application/pdf" });
        var reader = Service(db, "Student", storage: storage);
        (await reader.DownloadAsync(reference)).StatusCode.Should().Be(404);
        (await reader.ListAsync(null, null, 1, 20, false)).Data!.Items.Should().BeEmpty();
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var change = new LibraryDigitalMetadataRequestDto
        { Title = "Renewed", ExpiresOn = expiry, RowVersion = Convert.ToBase64String(document.RowVersion) };
        (await reader.UpdateMetadataAsync(reference, change)).StatusCode.Should().Be(403);
        var admin = Service(db, "TenantAdmin", storage: storage);
        var updated = await admin.UpdateMetadataAsync(reference, change);
        updated.Success.Should().BeTrue(updated.Message);
        updated.Data!.ExpiresOn.Should().Be(expiry);
        (await reader.DownloadAsync(reference)).Success.Should().BeTrue();
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "LibraryDigitalResource.Update");
        audit.OldValue.Should().Contain("Old");
        audit.NewValue.Should().Contain("Renewed");
        document.RowVersion = [4, 5, 6];
        await db.SaveChangesAsync();
        (await admin.UpdateMetadataAsync(reference, change)).StatusCode.Should().Be(409);
        (await admin.UpdateMetadataAsync(reference, new LibraryDigitalMetadataRequestDto
        { Title = "Invalid", ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
          RowVersion = Convert.ToBase64String(document.RowVersion) })).StatusCode.Should().Be(400);
        document.IsActive = false;
        await db.SaveChangesAsync();
        (await admin.UpdateMetadataAsync(reference, new LibraryDigitalMetadataRequestDto
        { Title = "Reactivation attempt", RowVersion = Convert.ToBase64String(document.RowVersion) }))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Employee_access_follows_effective_campus_assignment_and_employment_state()
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        var seed = await SeedAsync(db, reference);
        db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
        { TenantId = 101, Code = "LIBRARY_DIGITAL", Name = "Digital", IsActive = true });
        db.Documents.Add(new Document
        {
            TenantId = 101, PublicId = reference, FileAssetId = seed.Asset.Id,
            DocumentTypeCode = "LIBRARY_DIGITAL", EntityType = "LibraryBook",
            EntityId = seed.Book.Id, Title = "Staff handbook", IsActive = true
        });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employee = new Employee
        {
            TenantId = 101, PersonId = 19, UserId = 7, DesignationId = 1,
            EmployeeCode = "DIG-STAFF", FullName = "Digital reader",
            JoiningDate = today.AddDays(-30), State = EmployeeState.Active
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(seed.Asset.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = seed.Bytes, ContentType = "application/pdf" });
        var reader = Service(db, "Employee", storage: storage);
        (await reader.ListAsync(null, null, 1, 20, false)).StatusCode.Should().Be(403);
        (await reader.DownloadAsync(reference)).StatusCode.Should().Be(404);
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Never);
        var assignment = new EmployeeCampusAssignment
        {
            TenantId = 101, EmployeeId = employee.Id, CampusId = seed.Enrollment.CampusId,
            EffectiveFrom = today.AddDays(-1), EffectiveTo = today.AddDays(1), IsCurrent = true
        };
        db.EmployeeCampusAssignments.Add(assignment);
        await db.SaveChangesAsync();
        (await reader.ListAsync(null, null, 1, 20, false)).Data!.Items.Should().ContainSingle();
        (await reader.DownloadAsync(reference)).Success.Should().BeTrue();
        assignment.EffectiveTo = today.AddDays(-1);
        await db.SaveChangesAsync();
        (await reader.DownloadAsync(reference)).StatusCode.Should().Be(404);
        assignment.EffectiveTo = today.AddDays(1);
        employee.State = EmployeeState.Suspended;
        await db.SaveChangesAsync();
        (await reader.ListAsync(null, null, 1, 20, false)).StatusCode.Should().Be(403);
        (await reader.DownloadAsync(reference)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Ambiguous_private_file_owner_is_never_listed_or_downloaded()
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        var seed = await SeedAsync(db, reference);
        db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
        { TenantId = 101, Code = "LIBRARY_DIGITAL", Name = "Digital", IsActive = true });
        db.Documents.Add(new Document
        {
            TenantId = 101, PublicId = reference, FileAssetId = seed.Asset.Id,
            DocumentTypeCode = "LIBRARY_DIGITAL", EntityType = "LibraryBook",
            EntityId = seed.Book.Id, Title = "Library document", IsActive = true
        });
        db.Documents.Add(new Document
        {
            TenantId = 101, PublicId = Guid.NewGuid(), FileAssetId = seed.Asset.Id,
            DocumentTypeCode = "SENSITIVE", EntityType = "Student",
            EntityId = 999, Title = "Other owner", IsActive = true
        });
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        var reader = Service(db, "Student", storage: storage);
        (await reader.ListAsync(null, null, 1, 20, false)).Data!.Items.Should().BeEmpty();
        (await reader.DownloadAsync(reference)).StatusCode.Should().Be(404);
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Never);
        (await db.AuditLogs.AnyAsync(x => x.Action == "LibraryDigitalResource.Download"))
            .Should().BeFalse();
    }

}
