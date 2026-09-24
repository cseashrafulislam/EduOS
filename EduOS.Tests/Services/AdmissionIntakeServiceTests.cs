using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
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
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdmissionIntakeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Form_create_is_retry_safe_and_publish_is_concurrency_protected()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var refs = await SeedReferencesAsync(context, 101);
        var service = CreateService(context, new TestCurrentUser(101));
        var request = FormRequest(refs);

        var created = await service.CreateFormAsync(request);
        var replay = await service.CreateFormAsync(request);
        var conflicting = FormRequest(refs);
        conflicting.ClientRequestId = request.ClientRequestId;
        conflicting.Title = "Different form";
        var conflict = await service.CreateFormAsync(conflicting);

        created.StatusCode.Should().Be(201);
        replay.Data!.Id.Should().Be(created.Data!.Id);
        conflict.StatusCode.Should().Be(409);

        var row = await context.AdmissionIntakeForms.SingleAsync();
        row.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await context.SaveChangesAsync();
        var stale = await service.PublishFormAsync(row.Id, new AdmissionRowVersionDto { RowVersion = Convert.ToBase64String([9]) });
        var published = await service.PublishFormAsync(row.Id, new AdmissionRowVersionDto { RowVersion = Convert.ToBase64String(row.RowVersion) });

        stale.StatusCode.Should().Be(409);
        published.Data!.Status.Should().Be(AdmissionIntakeFormStatus.Published);
    }

    [Fact]
    public async Task Other_tenant_cannot_list_or_review_documents()
    {
        var options = CreateOptions();
        Guid applicantReference;
        long documentId;
        await using (var tenant202 = CreateContext(options, 202))
        {
            var refs = await SeedReferencesAsync(tenant202, 202);
            var form = await SeedPublishedFormAsync(tenant202, 202, refs);
            var applicant = await SeedApplicantAsync(tenant202, 202, refs, form);
            var document = new AdmissionApplicantDocument
            {
                TenantId = 202, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), ApplicantId = applicant.Id,
                AdmissionIntakeFormId = form.Id, DocumentType = "birth-certificate", OriginalFileName = "birth.pdf",
                StorageKey = "tenant-202/admissions/birth.pdf", ContentType = "application/pdf", FileSizeBytes = 4,
                Sha256 = Convert.ToBase64String(new byte[32]), IsCurrent = true, UploadedAtUtc = Now.UtcDateTime
            };
            tenant202.Add(document);
            await tenant202.SaveChangesAsync();
            applicantReference = applicant.PublicId;
            documentId = document.Id;
        }

        await using var tenant101 = CreateContext(options, 101);
        var service = CreateService(tenant101, new TestCurrentUser(101));
        (await service.GetDocumentsAsync(applicantReference)).StatusCode.Should().Be(404);
        (await service.ReviewDocumentAsync(applicantReference, documentId, new ReviewAdmissionDocumentDto
        {
            Status = AdmissionDocumentVerificationStatus.Verified,
            RowVersion = Convert.ToBase64String([1])
        })).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Officer_can_verify_and_download_only_current_document_with_row_version()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var refs = await SeedReferencesAsync(context, 101);
        var form = await SeedPublishedFormAsync(context, 101, refs);
        var applicant = await SeedApplicantAsync(context, 101, refs, form);
        var document = new AdmissionApplicantDocument
        {
            TenantId = 101, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), ApplicantId = applicant.Id,
            AdmissionIntakeFormId = form.Id, DocumentType = "birth-certificate", OriginalFileName = "birth.pdf",
            StorageKey = "tenant-101/admissions/birth.pdf", ContentType = "application/pdf", FileSizeBytes = 4,
            Sha256 = Convert.ToBase64String(new byte[32]), IsCurrent = true, UploadedAtUtc = Now.UtcDateTime
        };
        context.Add(document);
        await context.SaveChangesAsync();
        document.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await context.SaveChangesAsync();
        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.GetPrivateFileAsync(document.StorageKey)).ReturnsAsync(new FileDownloadResult
        {
            Content = "%PDF"u8.ToArray(), ContentType = "application/pdf", FileName = "stored.pdf"
        });
        var service = CreateService(context, new TestCurrentUser(101), storage.Object);

        var stale = await service.ReviewDocumentAsync(applicant.PublicId, document.Id, new ReviewAdmissionDocumentDto
        {
            Status = AdmissionDocumentVerificationStatus.Verified, RowVersion = Convert.ToBase64String([9])
        });
        var verified = await service.ReviewDocumentAsync(applicant.PublicId, document.Id, new ReviewAdmissionDocumentDto
        {
            Status = AdmissionDocumentVerificationStatus.Verified, RowVersion = Convert.ToBase64String(document.RowVersion)
        });
        var content = await service.GetDocumentContentAsync(applicant.PublicId, document.Id);

        stale.StatusCode.Should().Be(409);
        verified.Data!.VerificationStatus.Should().Be(AdmissionDocumentVerificationStatus.Verified);
        content.Data!.Content.Should().Equal("%PDF"u8.ToArray());
        content.Data.FileName.Should().Be("birth.pdf");
    }

    private static AdmissionIntakeService CreateService(EduOSDbContext context, ICurrentUserService user, IFileUploadService? storage = null) => new(
        new GenericRepository<AdmissionIntakeForm>(context), new GenericRepository<AdmissionApplicant>(context),
        new GenericRepository<AdmissionApplicantDocument>(context), new GenericRepository<AcademicYear>(context),
        new GenericRepository<AcademicTerm>(context), new GenericRepository<Campus>(context), new GenericRepository<Class>(context),
        context, user, storage ?? new Mock<IFileUploadService>().Object, new FixedTimeProvider(Now), NullLogger<AdmissionIntakeService>.Instance);

    internal static CreateAdmissionIntakeFormDto FormRequest(References refs) => new()
    {
        ClientRequestId = Guid.NewGuid(), Code = "admission-2027-main-six", Title = "Class Six Admission",
        AcademicYearId = refs.YearId, CampusId = refs.CampusId, AcademicUnitId = refs.UnitId,
        OpensAtUtc = Now.UtcDateTime.AddDays(-1), ClosesAtUtc = Now.UtcDateTime.AddDays(30), Currency = "BDT",
        Fields = [new AdmissionFormFieldDto { Key = "blood_group", Label = "Blood group", Type = AdmissionFormFieldType.Text, IsRequired = true, MaxLength = 5 }],
        DocumentRequirements = [new AdmissionDocumentRequirementDto { DocumentType = "birth-certificate", Label = "Birth certificate", IsRequired = true, MaxFileSizeMb = 5, AllowedExtensions = [".pdf"] }]
    };

    internal static async Task<AdmissionIntakeForm> SeedPublishedFormAsync(EduOSDbContext context, long tenantId, References refs)
    {
        var request = FormRequest(refs);
        var form = new AdmissionIntakeForm
        {
            TenantId = tenantId, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), Code = $"FORM-{tenantId}", Title = request.Title,
            AcademicYearId = refs.YearId, CampusId = refs.CampusId, AcademicUnitId = refs.UnitId,
            OpensAtUtc = request.OpensAtUtc, ClosesAtUtc = request.ClosesAtUtc, Currency = "BDT",
            FieldsJson = System.Text.Json.JsonSerializer.Serialize(request.Fields, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            DocumentRequirementsJson = System.Text.Json.JsonSerializer.Serialize(request.DocumentRequirements, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            Status = AdmissionIntakeFormStatus.Published, PublishedAtUtc = Now.UtcDateTime
        };
        context.Add(form);
        await context.SaveChangesAsync();
        return form;
    }

    internal static async Task<AdmissionApplicant> SeedApplicantAsync(EduOSDbContext context, long tenantId, References refs, AdmissionIntakeForm form)
    {
        var applicant = new AdmissionApplicant
        {
            TenantId = tenantId, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), ApplicationNumber = $"APP-{tenantId}",
            AdmissionIntakeFormId = form.Id, CustomResponsesJson = "{\"blood_group\":\"A+\"}", AcademicYearId = refs.YearId,
            CampusId = refs.CampusId, AcademicUnitId = refs.UnitId, ApplicantName = "Applicant", DateOfBirth = new DateTime(2012, 1, 1),
            Gender = Gender.Male, PrimaryMobile = "+8801712345678", PreferredLanguage = "bn-BD", Status = AdmissionApplicationStatus.UnderReview,
            SubmittedAtUtc = Now.UtcDateTime
        };
        context.Add(applicant);
        await context.SaveChangesAsync();
        return applicant;
    }

    internal static async Task<References> SeedReferencesAsync(EduOSDbContext context, long tenantId)
    {
        var year = new AcademicYear { TenantId = tenantId, Name = "2027", StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 12, 31), IsActive = true };
        var campus = new Campus { TenantId = tenantId, Name = "Main", Code = $"M-{tenantId}", IsActive = true };
        var unit = new Class { TenantId = tenantId, Name = "Class Six", NumericValue = 6, IsActive = true };
        context.AddRange(year, campus, unit);
        await context.SaveChangesAsync();
        return new References(year.Id, campus.Id, unit.Id);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"admission-intake-{Guid.NewGuid():N}").Options;

    internal static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin")], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    internal sealed record References(long YearId, long CampusId, long UnitId);
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    internal sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true; public long UserId => 7; public long TenantId => tenantId; public string? FullName => "Admission User";
        public string? Email => "admission@example.test"; public bool IsSuperAdmin => false; public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"]; public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1"; public string? UserAgent => "EduOS tests";
    }
}
