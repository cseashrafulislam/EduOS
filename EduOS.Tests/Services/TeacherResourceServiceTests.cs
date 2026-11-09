using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.LMS;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using System.Security.Cryptography;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TeacherResourceServiceTests
{
    private static readonly InMemoryDatabaseRoot Databases = new();

    private static EduOSDbContext Context(long tenant = 101, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(databaseName ?? "teacher-studio-" + Guid.NewGuid().ToString("N"), Databases).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "Teacher"), new Claim("TenantId", tenant.ToString())
        ], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class User(string role, long id = 7, long tenant = 101) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static TeacherResourceService Service(EduOSDbContext db, string role,
        long id = 7, long tenant = 101, Mock<IDocumentFileService>? documents = null,
        Mock<IFileUploadService>? storage = null) => new(db, new User(role, id, tenant),
            (documents ?? new Mock<IDocumentFileService>()).Object,
            (storage ?? new Mock<IFileUploadService>()).Object, TimeProvider.System,
            NullLogger<TeacherResourceService>.Instance);

    private static async Task<(long CampusId, long EmployeeId)> TeacherAsync(EduOSDbContext db)
    {
        var campus = new Campus { TenantId = 101, Name = "Dhaka", Code = "DHA", IsActive = true };
        var employee = new Employee { TenantId = 101, UserId = 7, FullName = "Teacher",
            EmployeeCode = "T-1", State = EmployeeState.Active, CanTeach = true };
        db.AddRange(campus, employee);
        await db.SaveChangesAsync();
        db.EmployeeCampusAssignments.Add(new EmployeeCampusAssignment
        {
            TenantId = 101, CampusId = campus.Id, EmployeeId = employee.Id,
            EffectiveFrom = new DateOnly(2025, 1, 1), IsCurrent = true
        });
        await db.SaveChangesAsync();
        return (campus.Id, employee.Id);
    }

    private static TeacherResourceUploadRequestDto UploadRequest(Guid reference, long campusId) => new()
    {
        RequestId = reference, CampusId = campusId, Title = "Class 9 worksheet",
        KindCode = "Worksheet", ScopeCode = "Campus", LanguageCode = "bn-BD",
        RightsHolder = "Dhaka School", LicenceCode = "InstitutionOwned",
        LicenceEvidence = "Approved internal authorship agreement"
    };

    private static async Task<(FileAsset Asset, byte[] Bytes, Mock<IDocumentFileService> Files,
        Mock<IFileUploadService> Storage)> StageAsync(EduOSDbContext db, Guid reference)
    {
        byte[] bytes = "%PDF-1.4\nResource"u8.ToArray();
        var asset = new FileAsset
        {
            TenantId = 101, PublicId = reference, StorageProvider = "LocalPrivate",
            StorageKey = "tenant-101/lms/teacher/resource.pdf", OriginalFileName = "resource.pdf",
            ContentType = "application/pdf", SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), UploadedByUserId = 7,
            MalwareScanStateCode = "Clean", IsVerifiedSafe = true,
            Visibility = FileVisibility.Private
        };
        db.FileAssets.Add(asset);
        await db.SaveChangesAsync();
        var files = new Mock<IDocumentFileService>();
        files.Setup(x => x.UploadForLmsAsync(reference, It.IsAny<PrivateFileUploadDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EduOS.Core.Common.ApiResponse<FileAssetDto>.SuccessResponse(new FileAssetDto
            { Id = asset.Id, Reference = reference }));
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(asset.StorageKey))
            .ReturnsAsync(new FileDownloadResult { Content = bytes, ContentType = asset.ContentType });
        return (asset, bytes, files, storage);
    }

    private static PrivateFileUploadDto UploadFile() => new()
    {
        FileName = "resource.pdf", ContentType = "application/pdf", Length = 9,
        Content = new MemoryStream("%PDF-1.4\n"u8.ToArray())
    };

    [Fact]
    public async Task Draft_is_private_until_independent_inspection_and_approval_then_withdrawn()
    {
        await using var db = Context();
        var teacher = await TeacherAsync(db);
        var reference = Guid.NewGuid();
        var stage = await StageAsync(db, reference);
        var owner = Service(db, "Teacher", documents: stage.Files, storage: stage.Storage);
        var reviewer = Service(db, "TenantAdmin", 8, storage: stage.Storage);
        var upload = await owner.UploadAsync(UploadRequest(reference, teacher.CampusId), UploadFile());
        upload.Success.Should().BeTrue(upload.Message);
        (await owner.UploadAsync(UploadRequest(reference, teacher.CampusId), UploadFile())).Success.Should().BeTrue();
        (await db.TeacherResources.CountAsync()).Should().Be(1);
        var row = await db.TeacherResources.SingleAsync();
        row.RowVersion = [1, 2, 3];
        await db.SaveChangesAsync();
        (await reviewer.ReviewAsync(reference, new TeacherResourceTransitionRequestDto
        { RowVersion = Convert.ToBase64String(row.RowVersion) }, true)).StatusCode.Should().Be(409);
        var submitted = await owner.SubmitAsync(reference, new TeacherResourceTransitionRequestDto
        { RowVersion = Convert.ToBase64String(row.RowVersion) });
        submitted.Success.Should().BeTrue(submitted.Message);
        (await reviewer.ReviewAsync(reference, new TeacherResourceTransitionRequestDto
        { RowVersion = Convert.ToBase64String(row.RowVersion) }, true)).StatusCode.Should().Be(409);
        (await reviewer.DownloadAsync(reference)).Success.Should().BeTrue();
        (await Service(db, "TenantAdmin", 7, storage: stage.Storage).ReviewAsync(reference,
            new TeacherResourceTransitionRequestDto
            { RowVersion = Convert.ToBase64String(row.RowVersion) }, true)).StatusCode.Should().Be(403);
        var approved = await reviewer.ReviewAsync(reference, new TeacherResourceTransitionRequestDto
        { RowVersion = Convert.ToBase64String(row.RowVersion) }, true);
        approved.Success.Should().BeTrue(approved.Message);
        (await owner.ListAsync(null, "Approved", 1, 20)).Data!.Items.Should().ContainSingle();
        (await owner.DownloadAsync(reference)).Success.Should().BeTrue();
        (await owner.WithdrawAsync(reference, new TeacherResourceTransitionRequestDto
        { RowVersion = Convert.ToBase64String(row.RowVersion), Note = "Outdated syllabus" })).Success.Should().BeTrue();
        (await reviewer.DownloadAsync(reference)).StatusCode.Should().Be(404);
        (await db.AuditLogs.CountAsync(x => x.EntityName == "TeacherResource")).Should().BeGreaterThan(3);
    }

    [Fact]
    public async Task Cross_tenant_and_unassigned_teacher_are_blocked_even_with_valid_ids()
    {
        await using var db = Context();
        var campus = await TeacherAsync(db);
        var reference = Guid.NewGuid();
        var stage = await StageAsync(db, reference);
        var outsider = Service(db, "Teacher", tenant: 102, documents: stage.Files);
        (await outsider.UploadAsync(UploadRequest(reference, campus.CampusId), UploadFile()))
            .StatusCode.Should().Be(403);
        var student = Service(db, "Student", documents: stage.Files);
        (await student.ListAsync(null, null, 1, 20)).StatusCode.Should().Be(403);
        var owner = Service(db, "Teacher", documents: stage.Files);
        var request = UploadRequest(reference, campus.CampusId + 100);
        (await owner.UploadAsync(request, UploadFile())).StatusCode.Should().Be(403);
        stage.Files.Verify(x => x.UploadForLmsAsync(It.IsAny<Guid>(),
            It.IsAny<PrivateFileUploadDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Metadata_invalid_rights_or_reused_request_never_registers_extra_resource()
    {
        await using var db = Context();
        var campus = await TeacherAsync(db);
        var reference = Guid.NewGuid();
        var stage = await StageAsync(db, reference);
        var owner = Service(db, "Teacher", documents: stage.Files);
        var request = UploadRequest(reference, campus.CampusId);
        request.LicenceEvidence = "   ";
        (await owner.UploadAsync(request, UploadFile())).StatusCode.Should().Be(400);
        request.LicenceEvidence = "Ownership confirmed";
        (await owner.UploadAsync(request, UploadFile())).Success.Should().BeTrue();
        request.Title = "Different content with same reference";
        (await owner.UploadAsync(request, UploadFile())).StatusCode.Should().Be(409);
        (await db.TeacherResources.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Public_catalogue_shares_only_licensed_links_and_enforces_live_withdrawal()
    {
        var databaseName = "teacher-public-" + Guid.NewGuid().ToString("N");
        await using var source = Context(101, databaseName);
        await using var db = Context(102, databaseName);
        source.Tenants.AddRange(
            new Tenant { Id = 101, Code = "SOURCE", Name = "Source school", Email = "source@example.org",
                State = TenantState.Active },
            new Tenant { Id = 102, Code = "READER", Name = "Reader school", Email = "reader@example.org",
                State = TenantState.Active });
        db.Employees.Add(new Employee
        {
            TenantId = 102, UserId = 7, EmployeeCode = "READER-1", FullName = "Reader",
            State = EmployeeState.Active, CanTeach = true
        });
        var asset = new FileAsset
        {
            TenantId = 101, PublicId = Guid.NewGuid(), StorageKey = "tenant-101/lms/source.pdf",
            StorageProvider = "LocalPrivate", ContentType = "application/pdf",
            OriginalFileName = "source.pdf", SizeBytes = 1024, IsVerifiedSafe = true,
            MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private
        };
        source.FileAssets.Add(asset);
        await source.SaveChangesAsync();
        await db.SaveChangesAsync();
        var publicReference = Guid.NewGuid();
        var publicRow = new TeacherResource
        {
            TenantId = 101, PublicId = publicReference, FileAssetId = asset.Id,
            AuthorEmployeeId = 42, CampusId = 43, Title = "Open classroom handout",
            KindCode = "Worksheet", StateCode = "Approved", ScopeCode = "Public",
            LanguageCode = "bn-BD", RightsHolder = "Source school",
            LicenceCode = "CreativeCommons", LicenceEvidence = "Reviewed CC-BY author declaration",
            SourceUrl = "https://education.example.org/cc-by-handout",
            PublishedAtUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        };
        source.TeacherResources.Add(publicRow);
        await source.SaveChangesAsync();
        var viewer = Service(db, "Teacher", tenant: 102);
        var result = await viewer.ListPublicAsync("classroom", 1, 20);
        result.Success.Should().BeTrue(result.Message);
        result.Data!.Items.Should().ContainSingle();
        result.Data.Items[0].Reference.Should().Be(publicReference);
        result.Data.Items[0].Institution.Should().Be("Source school");
        result.Data.Items[0].SourceUrl.Should().StartWith("https://");
        (await viewer.DownloadAsync(publicReference)).StatusCode.Should().Be(404);
        (await Service(db, "Student", tenant: 102).ListPublicAsync(null, 1, 20))
            .StatusCode.Should().Be(403);
        publicRow.StateCode = "Withdrawn";
        await source.SaveChangesAsync();
        (await viewer.ListPublicAsync(null, 1, 20)).Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Public_scope_rejects_institution_licence_and_missing_public_source()
    {
        await using var db = Context();
        var teacher = await TeacherAsync(db);
        var owner = Service(db, "Teacher");
        var request = UploadRequest(Guid.NewGuid(), teacher.CampusId);
        request.ScopeCode = "Public";
        (await owner.UploadAsync(request, UploadFile())).StatusCode.Should().Be(400);
        request.LicenceCode = "CreativeCommons";
        (await owner.UploadAsync(request, UploadFile())).StatusCode.Should().Be(400);
        request.SourceUrl = "http://untrusted.example.org/file";
        (await owner.UploadAsync(request, UploadFile())).StatusCode.Should().Be(400);
    }
}
