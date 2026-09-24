using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using EduOS.Service.Helpers.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public class PublicAdmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_exposes_only_published_assessment_result_for_verified_applicant()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"public-admission-{Guid.NewGuid():N}")
            .Options;
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        await using var context = new EduOSDbContext(options, http);
        var tenant = new Tenant
        {
            Name = "Example School",
            Code = "example-school",
            Email = "school@example.test",
            OwnerName = "Owner",
            IsActive = true,
            IsOnboardingComplete = true
        };
        var module = new ProductModule
        {
            Code = "ADMISSION",
            Name = "Admission",
            Category = "Academic",
            IsActive = true
        };
        context.AddRange(tenant, module);
        await context.SaveChangesAsync();

        context.TenantModules.Add(new TenantModule
        {
            TenantId = tenant.Id,
            ProductModuleId = module.Id,
            IsEnabled = true
        });
        var year = new AcademicYear
        {
            TenantId = tenant.Id,
            Name = "2026",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsActive = true
        };
        var campus = new Campus
        {
            TenantId = tenant.Id,
            Name = "Main Campus",
            Code = "MAIN",
            IsActive = true
        };
        var unit = new Class
        {
            TenantId = tenant.Id,
            Name = "Class Six",
            NumericValue = 6,
            IsActive = true
        };
        context.AddRange(year, campus, unit);
        await context.SaveChangesAsync();

        var applicant = new AdmissionApplicant
        {
            TenantId = tenant.Id,
            PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(),
            ApplicationNumber = "APP-2026-0001",
            AcademicYearId = year.Id,
            CampusId = campus.Id,
            AcademicUnitId = unit.Id,
            ApplicantName = "Applicant",
            DateOfBirth = new DateTime(2012, 1, 1),
            Gender = Gender.Male,
            PrimaryMobile = "+8801712345678",
            PreferredLanguage = "bn-BD",
            Status = AdmissionApplicationStatus.UnderReview,
            SubmittedAtUtc = Now.UtcDateTime
        };
        context.Add(applicant);
        await context.SaveChangesAsync();

        var publishedTest = new AdmissionTest
        {
            TenantId = tenant.Id,
            Name = "Admission Test",
            AcademicYearId = year.Id,
            CampusId = campus.Id,
            AcademicUnitId = unit.Id,
            TestDate = new DateTime(2026, 9, 20),
            TotalMarks = 100,
            PassMarks = 40,
            DurationMinutes = 60,
            IsPublished = true,
            PublishedAtUtc = Now.UtcDateTime
        };
        var unpublishedTest = new AdmissionTest
        {
            TenantId = tenant.Id,
            Name = "Private Draft Test",
            AcademicYearId = year.Id,
            CampusId = campus.Id,
            AcademicUnitId = unit.Id,
            TestDate = new DateTime(2026, 9, 21),
            TotalMarks = 100,
            PassMarks = 40,
            DurationMinutes = 60,
            IsPublished = false
        };
        context.AddRange(publishedTest, unpublishedTest);
        await context.SaveChangesAsync();

        context.AddRange(
            new AdmissionResult
            {
                TenantId = tenant.Id,
                AdmissionTestId = publishedTest.Id,
                ApplicantId = applicant.Id,
                ObtainedMarks = 88,
                Percentage = 88,
                IsPassed = true,
                MeritPosition = 2,
                ResultStatus = "Passed"
            },
            new AdmissionResult
            {
                TenantId = tenant.Id,
                AdmissionTestId = unpublishedTest.Id,
                ApplicantId = applicant.Id,
                ObtainedMarks = 99,
                Percentage = 99,
                IsPassed = true,
                MeritPosition = 1,
                ResultStatus = "Passed"
            });
        await context.SaveChangesAsync();

        var service = new PublicAdmissionService(
            new GenericRepository<Tenant>(context),
            new GenericRepository<TenantModule>(context),
            new GenericRepository<AdmissionApplicant>(context),
            new GenericRepository<AdmissionTest>(context),
            new GenericRepository<AdmissionResult>(context),
            new GenericRepository<AdmissionIntakeForm>(context),
            new GenericRepository<AdmissionApplicantDocument>(context),
            new GenericRepository<AcademicYear>(context),
            new GenericRepository<AcademicTerm>(context),
            new GenericRepository<Campus>(context),
            new GenericRepository<Class>(context),
            context,
            new Mock<IFileUploadService>().Object,
            http,
            new FixedTimeProvider(Now),
            NullLogger<PublicAdmissionService>.Instance);

        var result = await service.GetStatusAsync("example-school", applicant.PublicId, "01712345678");

        result.Success.Should().BeTrue();
        result.Data!.Assessment.Should().NotBeNull();
        result.Data.Assessment!.TestName.Should().Be("Admission Test");
        result.Data.Assessment.ObtainedMarks.Should().Be(88);
        result.Data.Assessment.MeritPosition.Should().Be(2);
    }

    [Fact]
    public async Task Status_requires_matching_mobile_even_with_valid_reference()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"public-admission-mobile-{Guid.NewGuid():N}")
            .Options;
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        await using var context = new EduOSDbContext(options, http);

        var tenant = new Tenant
        {
            Name = "Example School",
            Code = "example-school",
            Email = "school@example.test",
            OwnerName = "Owner",
            IsActive = true,
            IsOnboardingComplete = true
        };
        var module = new ProductModule { Code = "ADMISSION", Name = "Admission", Category = "Academic", IsActive = true };
        context.AddRange(tenant, module);
        await context.SaveChangesAsync();
        context.Add(new TenantModule { TenantId = tenant.Id, ProductModuleId = module.Id, IsEnabled = true });
        var applicant = new AdmissionApplicant
        {
            TenantId = tenant.Id,
            PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(),
            ApplicationNumber = "APP-2026-0001",
            ApplicantName = "Applicant",
            DateOfBirth = new DateTime(2012, 1, 1),
            Gender = Gender.Male,
            PrimaryMobile = "+8801712345678",
            PreferredLanguage = "bn-BD",
            Status = AdmissionApplicationStatus.Submitted,
            SubmittedAtUtc = Now.UtcDateTime
        };
        context.Add(applicant);
        await context.SaveChangesAsync();

        var service = new PublicAdmissionService(
            new GenericRepository<Tenant>(context),
            new GenericRepository<TenantModule>(context),
            new GenericRepository<AdmissionApplicant>(context),
            new GenericRepository<AdmissionTest>(context),
            new GenericRepository<AdmissionResult>(context),
            new GenericRepository<AdmissionIntakeForm>(context),
            new GenericRepository<AdmissionApplicantDocument>(context),
            new GenericRepository<AcademicYear>(context),
            new GenericRepository<AcademicTerm>(context),
            new GenericRepository<Campus>(context),
            new GenericRepository<Class>(context),
            context,
            new Mock<IFileUploadService>().Object,
            http,
            new FixedTimeProvider(Now),
            NullLogger<PublicAdmissionService>.Instance);

        var result = await service.GetStatusAsync("example-school", applicant.PublicId, "01700000000");

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Open_configured_form_validates_custom_responses_and_replays_exact_submission()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"public-admission-form-{Guid.NewGuid():N}").Options;
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        await using var context = new EduOSDbContext(options, http);
        var tenant = new Tenant { Name = "Form School", Code = "form-school", Email = "school@example.test", OwnerName = "Owner", IsActive = true, IsOnboardingComplete = true };
        var module = new ProductModule { Code = "ADMISSION", Name = "Admission", Category = "Academic", IsActive = true };
        context.AddRange(tenant, module);
        await context.SaveChangesAsync();
        http.HttpContext!.Items["TenantId"] = tenant.Id;
        context.Add(new TenantModule { TenantId = tenant.Id, ProductModuleId = module.Id, IsEnabled = true });
        var year = new AcademicYear { TenantId = tenant.Id, Name = "2027", StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 12, 31), IsActive = true };
        var campus = new Campus { TenantId = tenant.Id, Name = "Main", Code = "FORM-MAIN", IsActive = true };
        var unit = new Class { TenantId = tenant.Id, Name = "Class Six", NumericValue = 6, IsActive = true };
        context.AddRange(year, campus, unit);
        await context.SaveChangesAsync();
        var fields = new[] { new AdmissionFormFieldDto { Key = "blood_group", Label = "Blood group", Type = AdmissionFormFieldType.Text, IsRequired = true, MaxLength = 5 } };
        var form = new AdmissionIntakeForm
        {
            TenantId = tenant.Id, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), Code = "FORM-SIX", Title = "Class Six",
            AcademicYearId = year.Id, CampusId = campus.Id, AcademicUnitId = unit.Id, OpensAtUtc = Now.UtcDateTime.AddDays(-1),
            ClosesAtUtc = Now.UtcDateTime.AddDays(30), Currency = "BDT",
            FieldsJson = System.Text.Json.JsonSerializer.Serialize(fields, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            DocumentRequirementsJson = "[]", Status = AdmissionIntakeFormStatus.Published, PublishedAtUtc = Now.UtcDateTime
        };
        context.Add(form);
        await context.SaveChangesAsync();
        var service = new PublicAdmissionService(
            new GenericRepository<Tenant>(context), new GenericRepository<TenantModule>(context), new GenericRepository<AdmissionApplicant>(context),
            new GenericRepository<AdmissionTest>(context), new GenericRepository<AdmissionResult>(context), new GenericRepository<AdmissionIntakeForm>(context),
            new GenericRepository<AdmissionApplicantDocument>(context), new GenericRepository<AcademicYear>(context), new GenericRepository<AcademicTerm>(context),
            new GenericRepository<Campus>(context), new GenericRepository<Class>(context), context, new Mock<IFileUploadService>().Object, http,
            new FixedTimeProvider(Now), NullLogger<PublicAdmissionService>.Instance);
        var request = new CreateAdmissionApplicationDto
        {
            ClientRequestId = Guid.NewGuid(), AdmissionFormReference = form.PublicId, CustomResponses = new Dictionary<string, string?> { ["blood_group"] = "A+" },
            AcademicYearId = year.Id, CampusId = campus.Id, AcademicUnitId = unit.Id, ApplicantName = "Applicant",
            DateOfBirth = new DateTime(2010, 1, 1), Gender = Gender.Male, PrimaryMobile = "01712345678",
            GuardianName = "Guardian", GuardianRelation = "Parent", GuardianMobile = "01812345678", PreferredLanguage = "bn-BD"
        };

        var forms = await service.GetFormsAsync("form-school");
        var created = await service.CreateAsync("form-school", request);
        var replay = await service.CreateAsync("form-school", request);
        request.CustomResponses!["blood_group"] = "B+";
        var conflict = await service.CreateAsync("form-school", request);

        forms.Data.Should().ContainSingle(x => x.Reference == form.PublicId);
        created.StatusCode.Should().Be(201);
        replay.Data!.Reference.Should().Be(created.Data!.Reference);
        conflict.StatusCode.Should().Be(409);
        var storedResponses = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(
            (await context.AdmissionApplicants.SingleAsync()).CustomResponsesJson!);
        storedResponses.Should().ContainKey("blood_group").WhoseValue.Should().Be("A+");
    }

    [Fact]
    public async Task Configured_document_upload_is_private_retry_safe_and_visible_in_status()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"public-admission-document-{Guid.NewGuid():N}").Options;
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        await using var context = new EduOSDbContext(options, http);
        var tenant = new Tenant { Name = "Document School", Code = "document-school", Email = "school@example.test", OwnerName = "Owner", IsActive = true, IsOnboardingComplete = true };
        var module = new ProductModule { Code = "ADMISSION", Name = "Admission", Category = "Academic", IsActive = true };
        context.AddRange(tenant, module);
        await context.SaveChangesAsync();
        http.HttpContext!.Items["TenantId"] = tenant.Id;
        context.Add(new TenantModule { TenantId = tenant.Id, ProductModuleId = module.Id, IsEnabled = true });
        var year = new AcademicYear { TenantId = tenant.Id, Name = "2027", StartDate = new DateTime(2027, 1, 1), EndDate = new DateTime(2027, 12, 31), IsActive = true };
        var campus = new Campus { TenantId = tenant.Id, Name = "Main", Code = "MAIN", IsActive = true };
        var unit = new Class { TenantId = tenant.Id, Name = "Class Six", NumericValue = 6, IsActive = true };
        context.AddRange(year, campus, unit);
        await context.SaveChangesAsync();
        var requirements = new[] { new AdmissionDocumentRequirementDto { DocumentType = "birth-certificate", Label = "Birth certificate", IsRequired = true, MaxFileSizeMb = 5, AllowedExtensions = [".pdf"] } };
        var form = new AdmissionIntakeForm
        {
            TenantId = tenant.Id, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), Code = "2027-SIX", Title = "Class Six",
            AcademicYearId = year.Id, CampusId = campus.Id, AcademicUnitId = unit.Id, OpensAtUtc = Now.UtcDateTime.AddDays(-1),
            ClosesAtUtc = Now.UtcDateTime.AddDays(30), Currency = "BDT", FieldsJson = "[]",
            DocumentRequirementsJson = System.Text.Json.JsonSerializer.Serialize(requirements, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            Status = AdmissionIntakeFormStatus.Published, PublishedAtUtc = Now.UtcDateTime
        };
        context.Add(form);
        await context.SaveChangesAsync();
        var applicant = new AdmissionApplicant
        {
            TenantId = tenant.Id, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), ApplicationNumber = "APP-2027-DOC",
            AdmissionIntakeFormId = form.Id, AcademicYearId = year.Id, CampusId = campus.Id, AcademicUnitId = unit.Id,
            ApplicantName = "Applicant", DateOfBirth = new DateTime(2012, 1, 1), Gender = Gender.Male,
            PrimaryMobile = "+8801712345678", PreferredLanguage = "bn-BD", Status = AdmissionApplicationStatus.UnderReview, SubmittedAtUtc = Now.UtcDateTime
        };
        context.Add(applicant);
        await context.SaveChangesAsync();

        var storage = new Mock<IFileUploadService>();
        storage.Setup(x => x.UploadPrivateForTenantAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), tenant.Id))
            .ReturnsAsync(new FileUploadResult { Success = true, FileUrl = $"tenant-{tenant.Id}/admissions/birth.pdf", FileHash = Convert.ToBase64String(new byte[32]) });
        var service = new PublicAdmissionService(
            new GenericRepository<Tenant>(context), new GenericRepository<TenantModule>(context), new GenericRepository<AdmissionApplicant>(context),
            new GenericRepository<AdmissionTest>(context), new GenericRepository<AdmissionResult>(context), new GenericRepository<AdmissionIntakeForm>(context),
            new GenericRepository<AdmissionApplicantDocument>(context), new GenericRepository<AcademicYear>(context), new GenericRepository<AcademicTerm>(context),
            new GenericRepository<Campus>(context), new GenericRepository<Class>(context), context, storage.Object, http,
            new FixedTimeProvider(Now), NullLogger<PublicAdmissionService>.Instance);
        var bytes = "%PDF-1.7"u8.ToArray();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "birth.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        var request = new AdmissionDocumentUploadDto { ClientRequestId = Guid.NewGuid(), Mobile = "01712345678", DocumentType = "birth-certificate", File = file };

        var created = await service.UploadDocumentAsync("document-school", applicant.PublicId, request);
        var replay = await service.UploadDocumentAsync("document-school", applicant.PublicId, request);
        var status = await service.GetStatusAsync("document-school", applicant.PublicId, request.Mobile);

        created.StatusCode.Should().Be(201);
        replay.Data!.Id.Should().Be(created.Data!.Id);
        (await context.AdmissionApplicantDocuments.CountAsync()).Should().Be(1);
        (await context.AuditLogs.SingleAsync(x => x.TableName == nameof(AdmissionApplicantDocument))).NewValue.Should().BeNull();
        status.Data!.Documents.Should().ContainSingle(x => x.DocumentType == "birth-certificate" && x.VerificationStatus == AdmissionDocumentVerificationStatus.Pending);
        storage.Verify(x => x.UploadPrivateForTenantAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), tenant.Id), Times.Once);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
