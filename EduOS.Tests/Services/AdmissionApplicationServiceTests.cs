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
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdmissionApplicationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Submitted_application_belongs_to_tenant_and_persists_guardian_separately()
    {
        await using var db = Context(Options(), 101);
        var scope = await SeedAsync(db, 101);
        var result = await Service(db, new User(101)).CreateAsync(Request(scope));
        result.StatusCode.Should().Be(201);
        var applicant = await db.AdmissionApplicants.SingleAsync();
        applicant.TenantId.Should().Be(101);
        applicant.Phone.Should().Be("+8801712345678");
        applicant.State.Should().Be(AdmissionApplicantState.Submitted);
        applicant.DateOfBirth.Should().Be(new DateOnly(2012, 2, 3));
        (await db.Set<AdmissionApplicantGuardian>().SingleAsync()).Phone.Should().Be("01700000000");
        typeof(AdmissionApplicant).GetProperties().Select(x => x.Name)
            .Should().NotContain(x => x.Contains("Nid", StringComparison.OrdinalIgnoreCase) ||
                x.Contains("BirthCert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Same_request_replays_and_different_payload_is_rejected()
    {
        await using var db = Context(Options(), 101);
        var scope = await SeedAsync(db, 101);
        var service = Service(db, new User(101));
        var request = Request(scope);
        var first = await service.CreateAsync(request);
        var replay = await service.CreateAsync(request);
        request.ApplicantName = "Changed";
        var conflict = await service.CreateAsync(request);
        first.StatusCode.Should().Be(201);
        replay.Data!.Reference.Should().Be(first.Data!.Reference);
        conflict.StatusCode.Should().Be(409);
        (await db.AdmissionApplicants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Minor_without_guardian_contact_cannot_be_submitted()
    {
        await using var db = Context(Options(), 101);
        var scope = await SeedAsync(db, 101);
        var request = Request(scope);
        request.GuardianMobile = null;
        var response = await Service(db, new User(101)).CreateAsync(request);
        response.Success.Should().BeFalse();
        (await db.AdmissionApplicants.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Foreign_tenant_applicant_is_not_readable_and_foreign_form_is_rejected()
    {
        var options = Options();
        Guid reference;
        await using (var db202 = Context(options, 202))
        {
            var scope = await SeedAsync(db202, 202);
            reference = (await Service(db202, new User(202)).CreateAsync(Request(scope))).Data!.Reference;
        }
        await using var db101 = Context(options, 101);
        var own = await SeedAsync(db101, 101);
        var service = Service(db101, new User(101));
        (await service.GetByReferenceAsync(reference)).StatusCode.Should().Be(404);
        (await service.GetPageAsync(new AdmissionApplicationQueryDto())).Data!.Items.Should().BeEmpty();
        var invalid = Request(own);
        invalid.CampusId = own.CampusId + 9999;
        (await service.CreateAsync(invalid)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Review_enforces_state_transitions_and_row_version()
    {
        await using var db = Context(Options(), 101);
        var scope = await SeedAsync(db, 101);
        var service = Service(db, new User(101));
        var created = await service.CreateAsync(Request(scope));
        var stale = await service.ReviewAsync(created.Data!.Reference, new ReviewAdmissionApplicationDto
        {
            State = AdmissionApplicantState.UnderReview,
            RowVersion = Convert.ToBase64String([1])
        });
        stale.StatusCode.Should().Be(409);
        var unsupported = await service.ReviewAsync(created.Data.Reference, new ReviewAdmissionApplicationDto
        {
            State = AdmissionApplicantState.Admitted, RowVersion = created.Data.RowVersion
        });
        unsupported.StatusCode.Should().Be(400);
        var reviewed = await service.ReviewAsync(created.Data.Reference, new ReviewAdmissionApplicationDto
        {
            State = AdmissionApplicantState.UnderReview, RowVersion = created.Data.RowVersion
        });
        reviewed.Success.Should().BeTrue(reviewed.Message);
        var rejected = await service.ReviewAsync(created.Data.Reference, new ReviewAdmissionApplicationDto
        {
            State = AdmissionApplicantState.Rejected, RowVersion = reviewed.Data!.RowVersion
        });
        rejected.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Qualification_refuses_unverified_document()
    {
        await using var db = Context(Options(), 101);
        var scope = await SeedAsync(db, 101);
        var service = Service(db, new User(101));
        var created = await service.CreateAsync(Request(scope));
        var applicant = await db.AdmissionApplicants.SingleAsync();
        var asset = new FileAsset
        {
            TenantId = 101, StorageProvider = "PrivateFileSystem",
            StorageKey = "tenant-101/admissions/proof.pdf",
            OriginalFileName = "proof.pdf", ContentType = "application/pdf",
            SizeBytes = 100, IsVerifiedSafe = false
        };
        db.Add(asset); await db.SaveChangesAsync();
        db.Add(new AdmissionApplicantDocument
        {
            TenantId = 101, AdmissionApplicantId = applicant.Id,
            FileAssetId = asset.Id, DocumentTypeCode = "PROOF", IsVerified = false
        });
        await db.SaveChangesAsync();
        var result = await service.ReviewAsync(created.Data!.Reference, new ReviewAdmissionApplicationDto
        {
            State = AdmissionApplicantState.Qualified, RowVersion = created.Data.RowVersion
        });
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Teacher_role_cannot_access_admissions()
    {
        await using var db = Context(Options(), 101);
        (await Service(db, new User(101, "Teacher"))
            .GetPageAsync(new AdmissionApplicationQueryDto())).StatusCode.Should().Be(403);
    }

    private static AdmissionApplicationService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<AdmissionApplicant>(db),
        new GenericRepository<AdmissionIntakeForm>(db),
        new GenericRepository<AdmissionFormField>(db),
        new GenericRepository<AdmissionApplicantFieldValue>(db),
        new GenericRepository<AdmissionApplicantGuardian>(db),
        new GenericRepository<AdmissionApplicantDocument>(db),
        new GenericRepository<AdmissionDecision>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<Campus>(db),
        new GenericRepository<AcademicLevel>(db), db, user,
        new Clock(Now), NullLogger<AdmissionApplicationService>.Instance);

    private static CreateAdmissionApplicationDto Request(Scope scope) => new()
    {
        ClientRequestId = Guid.NewGuid(),
        AdmissionFormReference = scope.FormReference,
        AcademicYearId = scope.YearId, CampusId = scope.CampusId,
        AcademicLevelId = scope.LevelId,
        ApplicantName = "Rahim Uddin", ApplicantNameBangla = "রহিম উদ্দিন",
        DateOfBirth = new DateTime(2012, 2, 3),
        Gender = Gender.Male, PrimaryMobile = "+8801712345678",
        GuardianName = "Karim Uddin", GuardianRelation = "Father",
        GuardianMobile = "01700000000"
    };
    private static async Task<Scope> SeedAsync(EduOSDbContext db, long tenant)
    {
        var year = new AcademicYear { TenantId = tenant, Name = "2026", Code = "Y2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        var campus = new Campus { TenantId = tenant, Name = "Main", Code = "MAIN" };
        var program = new AcademicProgram { TenantId = tenant, Name = "School", Code = "SCHOOL" };
        db.AddRange(year, campus, program); await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = tenant, AcademicProgramId = program.Id,
            Name = "Class Six", Code = "C6", LevelNo = 6 };
        db.Add(level); await db.SaveChangesAsync();
        var form = new AdmissionIntakeForm
        {
            TenantId = tenant, PublicId = Guid.NewGuid(), Title = "Primary Intake",
            Code = "PRIMARY", CampusId = campus.Id, AcademicYearId = year.Id,
            AcademicProgramId = program.Id, AcademicLevelId = level.Id,
            OpensAt = Now.UtcDateTime.AddDays(-1), ClosesAt = Now.UtcDateTime.AddDays(30),
            State = AdmissionFormState.Published
        };
        db.Add(form); await db.SaveChangesAsync();
        return new Scope(year.Id, campus.Id, level.Id, form.PublicId);
    }
    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(
            "admission-application-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private sealed record Scope(long YearId, long CampusId, long LevelId, Guid FormReference);
    private sealed class Clock(DateTimeOffset time) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => time;
    }
    private sealed class User(long tenant, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Administrator";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
