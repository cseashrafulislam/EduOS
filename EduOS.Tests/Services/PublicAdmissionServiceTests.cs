using EduOS.Core.DTOs.Admission;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
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

public sealed class PublicAdmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Status_exposes_only_published_assessment_result_for_verified_applicant()
    {
        await using var fixture = await Fixture.CreateAsync("result-school");
        var applicant = await fixture.CreateApplicantAsync(AdmissionApplicantState.UnderReview);
        var published = new AdmissionTest
        {
            TenantId = fixture.Tenant.Id, AdmissionIntakeFormId = fixture.Form.Id,
            Name = "Published Admission Test", TestDate = new DateOnly(2026, 9, 20),
            TotalMarks = 100m, PassMarks = 40m, DurationMinutes = 60, IsPublished = true
        };
        var draft = new AdmissionTest
        {
            TenantId = fixture.Tenant.Id, AdmissionIntakeFormId = fixture.Form.Id,
            Name = "Unpublished Test", TestDate = new DateOnly(2026, 9, 21),
            TotalMarks = 100m, PassMarks = 40m, DurationMinutes = 60, IsPublished = false
        };
        fixture.Db.AddRange(published, draft);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.AddRange(
            new AdmissionResult
            {
                TenantId = fixture.Tenant.Id, AdmissionTestId = published.Id,
                AdmissionApplicantId = applicant.Id, ObtainedMarks = 88m,
                IsPassed = true, MeritPosition = 2
            },
            new AdmissionResult
            {
                TenantId = fixture.Tenant.Id, AdmissionTestId = draft.Id,
                AdmissionApplicantId = applicant.Id, ObtainedMarks = 99m,
                IsPassed = true, MeritPosition = 1
            });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.GetStatusAsync(fixture.Tenant.Code,
            applicant.PublicId, "01712345678");

        result.Success.Should().BeTrue($"status {result.StatusCode}: {result.Message}");
        result.Data!.Assessment.Should().NotBeNull();
        result.Data.Assessment!.TestName.Should().Be("Published Admission Test");
        result.Data.Assessment.ObtainedMarks.Should().Be(88m);
        result.Data.Assessment.MeritPosition.Should().Be(2);
        result.Data.MaskedMobile.Should().EndWith("5678").And.NotBe("+8801712345678");
    }

    [Fact]
    public async Task Status_requires_matching_mobile_and_rejects_other_tenant_reference()
    {
        await using var own = await Fixture.CreateAsync("status-school");
        var applicant = await own.CreateApplicantAsync();
        var wrongMobile = await own.Service.GetStatusAsync(own.Tenant.Code,
            applicant.PublicId, "01700000000");
        var unknown = await own.Service.GetStatusAsync(own.Tenant.Code,
            Guid.NewGuid(), "01712345678");
        wrongMobile.StatusCode.Should().Be(404);
        unknown.StatusCode.Should().Be(404);

        await using var other = await Fixture.CreateAsync("another-school");
        var crossTenant = await other.Service.GetStatusAsync(other.Tenant.Code,
            applicant.PublicId, "01712345678");
        crossTenant.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Published_form_validates_custom_fields_and_replays_only_matching_submission()
    {
        await using var fixture = await Fixture.CreateAsync("form-school");
        fixture.Db.AdmissionFormFields.Add(new AdmissionFormField
        {
            TenantId = fixture.Tenant.Id, AdmissionIntakeFormId = fixture.Form.Id,
            FieldKey = "blood_group", Label = "Blood group",
            DataType = CustomFieldDataType.Text, IsRequired = true
        });
        await fixture.Db.SaveChangesAsync();
        var request = fixture.CreateApplicationRequest();

        var forms = await fixture.Service.GetFormsAsync(fixture.Tenant.Code);
        var missingRequired = await fixture.Service.CreateAsync(fixture.Tenant.Code, request);
        request.CustomResponses = new Dictionary<string, string?> { ["blood_group"] = "A+" };
        var created = await fixture.Service.CreateAsync(fixture.Tenant.Code, request);
        var replay = await fixture.Service.CreateAsync(fixture.Tenant.Code, request);
        request.CustomResponses["blood_group"] = "B+";
        var changed = await fixture.Service.CreateAsync(fixture.Tenant.Code, request);

        forms.Success.Should().BeTrue($"forms {forms.StatusCode}: {forms.Message}");
        forms.Data.Should().ContainSingle(x => x.Reference == fixture.Form.PublicId);
        forms.Data!.Single().Fields.Should().ContainSingle(x => x.FieldKey == "blood_group");
        missingRequired.Success.Should().BeFalse();
        created.StatusCode.Should().Be(201, $"creation response: {created.Message}");
        replay.Success.Should().BeTrue();
        replay.Data!.Reference.Should().Be(created.Data!.Reference);
        changed.StatusCode.Should().Be(409);
        (await fixture.Db.AdmissionApplicants.CountAsync()).Should().Be(1);
        var saved = await fixture.Db.AdmissionApplicantFieldValues
            .Join(fixture.Db.AdmissionFormFields, x => x.AdmissionFormFieldId, x => x.Id,
                (x, field) => new { x.Value, field.FieldKey }).SingleAsync();
        saved.FieldKey.Should().Be("blood_group");
        saved.Value.Should().Be("A+");
    }

    [Fact]
    public async Task Configured_document_upload_is_private_retry_safe_and_visible_to_verified_applicant()
    {
        await using var fixture = await Fixture.CreateAsync("document-school");
        var applicant = await fixture.CreateApplicantAsync(AdmissionApplicantState.UnderReview);
        fixture.Db.Add(new DocumentTypeDefinition
        {
            TenantId = fixture.Tenant.Id, Code = "BIRTH-CERTIFICATE",
            Name = "Birth certificate", RequiresVerification = true, IsActive = true
        });
        await fixture.Db.SaveChangesAsync();
        var fileBytes = "%PDF-1.7\nexample test document"u8.ToArray();
        fixture.Storage.Setup(x => x.ValidateFile(It.IsAny<IFormFile>())).Returns(true);
        fixture.Storage.Setup(x => x.UploadPrivateForTenantAsync(
                It.IsAny<IFormFile>(), It.IsAny<string>(), fixture.Tenant.Id))
            .ReturnsAsync(new FileUploadResult
            {
                Success = true, FileUrl = $"tenant-{fixture.Tenant.Id}/admissions/birth.pdf"
            });
        var request = new AdmissionDocumentUploadDto
        {
            ClientRequestId = Guid.NewGuid(), Mobile = "01712345678",
            DocumentType = "birth-certificate",
            File = new PrivateFileUploadDto
            {
                FileName = "birth.pdf", ContentType = "application/pdf",
                Length = fileBytes.Length, Content = new MemoryStream(fileBytes)
            }
        };

        var created = await fixture.Service.UploadDocumentAsync(fixture.Tenant.Code,
            applicant.PublicId, request);
        request.File.Content.Position = 0;
        var replay = await fixture.Service.UploadDocumentAsync(fixture.Tenant.Code,
            applicant.PublicId, request);
        var status = await fixture.Service.GetStatusAsync(fixture.Tenant.Code,
            applicant.PublicId, request.Mobile);

        created.StatusCode.Should().Be(201);
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(created.Data!.Id);
        (await fixture.Db.AdmissionApplicantDocuments.CountAsync()).Should().Be(1);
        var asset = await fixture.Db.Set<FileAsset>().SingleAsync();
        asset.Visibility.Should().Be(FileVisibility.Private);
        asset.StorageKey.Should().Be($"tenant-{fixture.Tenant.Id}/admissions/birth.pdf");
        asset.Sha256.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileBytes)));
        status.Data!.Documents.Should().ContainSingle(x =>
            x.DocumentTypeCode == "BIRTH-CERTIFICATE" && !x.IsVerified);
        fixture.Storage.Verify(x => x.UploadPrivateForTenantAsync(
            It.IsAny<IFormFile>(), It.IsAny<string>(), fixture.Tenant.Id), Times.Once);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public EduOSDbContext Db { get; }
        public Tenant Tenant { get; private set; } = null!;
        public AdmissionIntakeForm Form { get; private set; } = null!;
        public PublicAdmissionService Service { get; private set; } = null!;
        public Mock<IFileUploadService> Storage { get; } = new();
        private readonly HttpContextAccessor _http;

        private Fixture(string databaseName)
        {
            _http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var options = new DbContextOptionsBuilder<EduOSDbContext>()
                .UseInMemoryDatabase("public-admission-" + databaseName + "-" + Guid.NewGuid().ToString("N"))
                .Options;
            Db = new EduOSDbContext(options, _http);
        }

        public static async Task<Fixture> CreateAsync(string tenantCode)
        {
            var f = new Fixture(tenantCode);
            f.Tenant = new Tenant
            {
                Name = "Example School", Code = tenantCode, Email = "admin@example.test",
                State = TenantState.Active, OnboardingStage = OnboardingStage.Completed,
                OnboardingCompletedAt = Now.UtcDateTime, EmailVerifiedAt = Now.UtcDateTime
            };
            f.Db.Tenants.Add(f.Tenant);
            var module = new ProductModule { Code = "ADMISSION", Name = "Admission", IsActive = true };
            var plan = new SubscriptionPlan { Code = "TRIAL", Name = "Trial" };
            f.Db.AddRange(module, plan);
            await f.Db.SaveChangesAsync();
            f._http.HttpContext!.Items["TenantId"] = f.Tenant.Id;
            f.Db.AddRange(
                new TenantModule
                {
                    TenantId = f.Tenant.Id, ProductModuleId = module.Id, IsEnabled = true
                },
                new TenantSubscription
                {
                    TenantId = f.Tenant.Id, SubscriptionPlanId = plan.Id,
                    State = SubscriptionState.Active,
                    StartsAt = Now.UtcDateTime.AddDays(-10),
                    EndsAt = Now.UtcDateTime.AddDays(60)
                });
            var campus = new Campus
            {
                TenantId = f.Tenant.Id, Name = "Main Campus", Code = "MAIN", IsActive = true
            };
            var program = new AcademicProgram
            {
                TenantId = f.Tenant.Id, Name = "School", Code = "SCH", IsActive = true
            };
            var year = new AcademicYear
            {
                TenantId = f.Tenant.Id, Name = "2026", Code = "AY-2026",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31), IsActive = true
            };
            f.Db.AddRange(campus, program, year);
            await f.Db.SaveChangesAsync();
            var level = new AcademicLevel
            {
                TenantId = f.Tenant.Id, AcademicProgramId = program.Id,
                Name = "Class Six", Code = "SIX", LevelNo = 6, IsActive = true
            };
            f.Db.Add(level);
            await f.Db.SaveChangesAsync();
            f.Form = new AdmissionIntakeForm
            {
                TenantId = f.Tenant.Id, PublicId = Guid.NewGuid(),
                Code = "FORM-SIX", Title = "Class Six",
                CampusId = campus.Id, AcademicYearId = year.Id,
                AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                State = AdmissionFormState.Published,
                OpensAt = Now.UtcDateTime.AddDays(-1),
                ClosesAt = Now.UtcDateTime.AddDays(30), CurrencyCode = "BDT"
            };
            f.Db.Add(f.Form);
            await f.Db.SaveChangesAsync();
            (await f.Db.Tenants.IgnoreQueryFilters().AnyAsync(x =>
                x.Id == f.Tenant.Id && x.State == TenantState.Active &&
                x.OnboardingStage == OnboardingStage.Completed &&
                x.OnboardingCompletedAt.HasValue)).Should().BeTrue("fixture tenant must be public-ready");
            (await f.Db.TenantSubscriptions.AnyAsync(x =>
                x.TenantId == f.Tenant.Id && x.State == SubscriptionState.Active &&
                x.StartsAt <= Now.UtcDateTime && x.EndsAt > Now.UtcDateTime))
                .Should().BeTrue("fixture subscription must be active");
            (await (from selected in f.Db.TenantModules
                join product in f.Db.ProductModules on selected.ProductModuleId equals product.Id
                where selected.TenantId == f.Tenant.Id && selected.IsEnabled &&
                    product.IsActive && product.Code == "ADMISSION"
                select selected.Id).AnyAsync()).Should().BeTrue("admission module must be enabled");
            f.Service = new PublicAdmissionService(
                new GenericRepository<Tenant>(f.Db),
                new GenericRepository<TenantModule>(f.Db),
                new GenericRepository<ProductModule>(f.Db),
                new GenericRepository<TenantSubscription>(f.Db),
                new GenericRepository<AdmissionIntakeForm>(f.Db),
                new GenericRepository<AdmissionFormField>(f.Db),
                new GenericRepository<AdmissionApplicant>(f.Db),
                new GenericRepository<AdmissionApplicantFieldValue>(f.Db),
                new GenericRepository<AdmissionApplicantGuardian>(f.Db),
                new GenericRepository<AdmissionApplicantDocument>(f.Db),
                new GenericRepository<DocumentTypeDefinition>(f.Db),
                new GenericRepository<FileAsset>(f.Db),
                new GenericRepository<Person>(f.Db),
                new GenericRepository<AdmissionResult>(f.Db),
                new GenericRepository<AdmissionTest>(f.Db),
                new GenericRepository<AdmissionDecision>(f.Db),
                new GenericRepository<AcademicYear>(f.Db),
                new GenericRepository<AcademicTerm>(f.Db),
                new GenericRepository<Campus>(f.Db),
                new GenericRepository<AcademicLevel>(f.Db),
                f.Db, f.Storage.Object, f._http,
                new FixedTimeProvider(Now), NullLogger<PublicAdmissionService>.Instance);
            return f;
        }

        public async Task<AdmissionApplicant> CreateApplicantAsync(
            AdmissionApplicantState state = AdmissionApplicantState.Submitted)
        {
            var applicant = new AdmissionApplicant
            {
                TenantId = Tenant.Id, ClientRequestId = Guid.NewGuid(),
                PublicId = Guid.NewGuid(), AdmissionIntakeFormId = Form.Id,
                ApplicationNumber = "APP-2026-0001", FullName = "Applicant",
                DateOfBirth = new DateOnly(2012, 1, 1), Gender = "Male",
                Phone = "+8801712345678", State = state, SubmittedAt = Now.UtcDateTime
            };
            Db.Add(applicant);
            await Db.SaveChangesAsync();
            return applicant;
        }

        public CreateAdmissionApplicationDto CreateApplicationRequest() => new()
        {
            ClientRequestId = Guid.NewGuid(), AdmissionFormReference = Form.PublicId,
            AcademicYearId = Form.AcademicYearId, AcademicTermId = Form.AcademicTermId,
            CampusId = Form.CampusId, AcademicLevelId = Form.AcademicLevelId,
            ApplicantName = "Applicant", DateOfBirth = new DateTime(2012, 1, 1),
            Gender = Gender.Male, PrimaryMobile = "01712345678",
            GuardianName = "Guardian", GuardianRelation = "Parent",
            GuardianMobile = "01812345678", PreferredLanguage = "bn-BD"
        };

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
