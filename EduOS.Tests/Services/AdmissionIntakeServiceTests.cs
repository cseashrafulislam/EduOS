using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdmissionIntakeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Form_code_is_unique_and_publication_requires_current_version()
    {
        await using var db = Context(Options(), 101);
        var scope = await SeedReferencesAsync(db, 101);
        var service = Service(db, new TestUser(101));
        var request = FormRequest(scope);
        var created = await service.CreateFormAsync(request);
        var duplicate = await service.CreateFormAsync(request);
        created.StatusCode.Should().Be(201);
        duplicate.StatusCode.Should().Be(409);
        var form = await db.AdmissionIntakeForms.SingleAsync();
        form.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await db.SaveChangesAsync();
        (await service.PublishFormAsync(form.Id, new AdmissionRowVersionDto
        { RowVersion = Convert.ToBase64String([9]) })).StatusCode.Should().Be(409);
        var published = await service.PublishFormAsync(form.Id,
            new AdmissionRowVersionDto { RowVersion = Convert.ToBase64String(form.RowVersion) });
        published.Success.Should().BeTrue(published.Message);
        published.Data!.State.Should().Be(AdmissionFormState.Published);
    }

    [Fact]
    public async Task Document_listing_and_review_do_not_cross_tenant_boundaries()
    {
        var options = Options();
        Guid reference; long docId;
        await using (var foreign = Context(options, 202))
        {
            var refs = await SeedReferencesAsync(foreign, 202);
            var form = await SeedPublishedFormAsync(foreign, 202, refs);
            var applicant = await SeedApplicantAsync(foreign, 202, form);
            var doc = await SeedDocumentAsync(foreign, 202, applicant, true);
            reference = applicant.PublicId; docId = doc.Id;
        }
        await using var own = Context(options, 101);
        var service = Service(own, new TestUser(101));
        (await service.GetDocumentsAsync(reference)).StatusCode.Should().Be(404);
        (await service.ReviewDocumentAsync(reference, docId, new ReviewAdmissionDocumentDto
        { Status = AdmissionDocumentVerificationStatus.Verified,
            RowVersion = Convert.ToBase64String([1]) })).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Cleared_private_file_requires_current_row_version_for_verification_and_download()
    {
        await using var db = Context(Options(), 101);
        var refs = await SeedReferencesAsync(db, 101);
        var form = await SeedPublishedFormAsync(db, 101, refs);
        var applicant = await SeedApplicantAsync(db, 101, form);
        var doc = await SeedDocumentAsync(db, 101, applicant, true);
        doc.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await db.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        var asset = await db.FileAssets.SingleAsync();
        storage.Setup(x => x.GetPrivateFileAsync(asset.StorageKey)).ReturnsAsync(new FileDownloadResult
        { Content = "%PDF"u8.ToArray(), ContentType = "application/pdf", FileName = "birth.pdf" });
        var service = Service(db, new TestUser(101), storage.Object);
        (await service.ReviewDocumentAsync(applicant.PublicId, doc.Id, new ReviewAdmissionDocumentDto
        { Status = AdmissionDocumentVerificationStatus.Verified,
            RowVersion = Convert.ToBase64String([9]) })).StatusCode.Should().Be(409);
        var verified = await service.ReviewDocumentAsync(applicant.PublicId, doc.Id,
            new ReviewAdmissionDocumentDto { Status = AdmissionDocumentVerificationStatus.Verified,
                RowVersion = Convert.ToBase64String(doc.RowVersion) });
        verified.Success.Should().BeTrue(verified.Message);
        verified.Data!.IsVerified.Should().BeTrue();
        var download = await service.GetDocumentContentAsync(applicant.PublicId, doc.Id);
        download.Success.Should().BeTrue();
        download.Data!.Content.Should().Equal("%PDF"u8.ToArray());
        download.Data.FileName.Should().Be("birth.pdf");
    }

    private static AdmissionIntakeService Service(EduOSDbContext db, ICurrentUserService user,
        IFileUploadService? storage = null) => new(
        new GenericRepository<AdmissionIntakeForm>(db),
        new GenericRepository<AdmissionFormField>(db),
        new GenericRepository<AdmissionApplicant>(db),
        new GenericRepository<AdmissionApplicantDocument>(db),
        new GenericRepository<FileAsset>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<AcademicLevel>(db),
        db, user, storage ?? new Mock<IFileUploadService>().Object,
        new FixedClock(Now), NullLogger<AdmissionIntakeService>.Instance);

    private static CreateAdmissionIntakeFormDto FormRequest(Scope scope) => new()
    {
        ClientRequestId = Guid.NewGuid(), Code = "intake_2026", Title = "Class Six Admission",
        AcademicYearId = scope.YearId, CampusId = scope.CampusId, AcademicLevelId = scope.LevelId,
        OpensAtUtc = Now.UtcDateTime.AddDays(-1), ClosesAtUtc = Now.UtcDateTime.AddDays(30),
        Currency = "BDT",
        Fields = [new AdmissionFormFieldDto
        { FieldKey = "blood_group", Label = "Blood group",
            DataType = CustomFieldDataType.Text, IsRequired = true, IsActive = true }]
    };

    private static async Task<Scope> SeedReferencesAsync(EduOSDbContext db, long tenant)
    {
        var year = new AcademicYear { TenantId = tenant, Code = "Y2026", Name = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        var campus = new Campus { TenantId = tenant, Name = "Main", Code = "MAIN" };
        var program = new AcademicProgram { TenantId = tenant, Code = "SCHOOL", Name = "School" };
        db.AddRange(year, campus, program);
        await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = tenant, AcademicProgramId = program.Id,
            Name = "Class Six", Code = "C6", LevelNo = 6 };
        db.Add(level); await db.SaveChangesAsync();
        return new Scope(year.Id, campus.Id, program.Id, level.Id);
    }

    private static async Task<AdmissionIntakeForm> SeedPublishedFormAsync(EduOSDbContext db,
        long tenant, Scope scope)
    {
        var form = new AdmissionIntakeForm
        {
            TenantId = tenant, Code = "PUBLISHED", Title = "Open Intake",
            CampusId = scope.CampusId, AcademicYearId = scope.YearId,
            AcademicLevelId = scope.LevelId, AcademicProgramId = scope.ProgramId,
            State = AdmissionFormState.Published, OpensAt = Now.UtcDateTime.AddDays(-1),
            ClosesAt = Now.UtcDateTime.AddDays(30)
        };
        db.Add(form); await db.SaveChangesAsync();
        return form;
    }

    private static async Task<AdmissionApplicant> SeedApplicantAsync(EduOSDbContext db,
        long tenant, AdmissionIntakeForm form)
    {
        var row = new AdmissionApplicant
        {
            TenantId = tenant, AdmissionIntakeFormId = form.Id,
            ClientRequestId = Guid.NewGuid(), ApplicationNumber = "APP-" + Guid.NewGuid().ToString("N"),
            FullName = "Applicant", Phone = "+8801712345678",
            DateOfBirth = new DateOnly(2012, 1, 1), Gender = "Male",
            State = AdmissionApplicantState.Submitted, SubmittedAt = Now.UtcDateTime
        };
        db.Add(row); await db.SaveChangesAsync();
        return row;
    }

    private static async Task<AdmissionApplicantDocument> SeedDocumentAsync(EduOSDbContext db,
        long tenant, AdmissionApplicant applicant, bool safe)
    {
        var asset = new FileAsset
        {
            TenantId = tenant, StorageProvider = "PrivateFileSystem",
            StorageKey = "tenant-" + tenant + "/admissions/birth.pdf",
            OriginalFileName = "birth.pdf", ContentType = "application/pdf",
            SizeBytes = 4, Sha256 = Convert.ToHexString(new byte[32]),
            IsVerifiedSafe = safe
        };
        db.Add(asset); await db.SaveChangesAsync();
        var doc = new AdmissionApplicantDocument
        {
            TenantId = tenant, AdmissionApplicantId = applicant.Id,
            FileAssetId = asset.Id, DocumentTypeCode = "BIRTH_CERTIFICATE",
            VersionNo = 1, IsVerified = false
        };
        db.Add(doc); await db.SaveChangesAsync();
        return doc;
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("admission-intake-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record Scope(long YearId, long CampusId, long ProgramId, long LevelId);
    private sealed class FixedClock(DateTimeOffset date) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => date; }
    private sealed class TestUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Admission";
        public string? Email => "officer@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
