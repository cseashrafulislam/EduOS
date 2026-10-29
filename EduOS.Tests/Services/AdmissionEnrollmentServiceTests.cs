using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class AdmissionEnrollmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Qualified_applicant_with_accepted_offer_creates_canonical_student_enrollment_and_guardian()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        var service = CreateService(context, new TestCurrentUser(101));
        var result = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.StudentCode.Should().Be("STU-" + seed.Application.PublicId.ToString("N").ToUpperInvariant());
        (await context.Set<Student>().CountAsync()).Should().Be(1);
        (await context.Set<StudentEnrollment>().CountAsync()).Should().Be(1);
        (await context.Set<Guardian>().CountAsync()).Should().Be(1);
        (await context.Set<StudentGuardian>().CountAsync()).Should().Be(1);
        (await context.Set<Person>().CountAsync()).Should().Be(2);
        (await context.Set<StudentPersonLink>().CountAsync()).Should().Be(1);
        seed.Application.State.Should().Be(AdmissionApplicantState.Admitted);
    }

    [Fact]
    public async Task Repeated_admit_is_idempotent_and_does_not_create_duplicate_student()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        var service = CreateService(context, new TestCurrentUser(101));
        var first = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        var second = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.Data!.StudentId.Should().Be(first.Data!.StudentId);
        (await context.Set<Student>().CountAsync()).Should().Be(1);
        (await context.Set<StudentEnrollment>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Under_review_applicant_and_foreign_batch_are_rejected()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.UnderReview);
        var service = CreateService(context, new TestCurrentUser(101));
        var rejectedState = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        rejectedState.Success.Should().BeFalse();
        rejectedState.StatusCode.Should().Be(409);

        seed.Application.State = AdmissionApplicantState.Qualified;
        await context.SaveChangesAsync();
        var request = Request(seed); request.AcademicBatchId += 9999;
        var invalidBatch = await service.AdmitAsync(seed.Application.PublicId, request);
        invalidBatch.Success.Should().BeFalse();
        invalidBatch.StatusCode.Should().Be(409);
        (await context.Set<Student>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Zero_capacity_batch_never_admits_unlimited_students()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        seed.Batch.Capacity = 0;
        await context.SaveChangesAsync();
        var service = CreateService(context, new TestCurrentUser(101));
        var rejected = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        rejected.StatusCode.Should().Be(409);
        (await context.Set<Student>().CountAsync()).Should().Be(0);
        (await context.Set<StudentEnrollment>().CountAsync()).Should().Be(0);
        seed.Application.State.Should().Be(AdmissionApplicantState.Qualified);
    }

    [Fact]
    public async Task User_without_admission_permission_cannot_convert_applicant()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        var service = CreateService(context, new TestCurrentUser(101, "Teacher"));
        var result = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        result.StatusCode.Should().Be(403);
        (await context.Set<Student>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Offer_issuance_is_idempotent_and_does_not_bypass_verified_acceptance()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        context.RemoveRange(context.Set<AdmissionDecision>());
        await context.SaveChangesAsync();
        var service = CreateService(context, new TestCurrentUser(101));
        var request = OfferRequest(seed);
        var first = await service.IssueOfferAsync(seed.Application.PublicId, request);
        var second = await service.IssueOfferAsync(seed.Application.PublicId, request);
        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        first.Data!.Id.Should().Be(second.Data!.Id);
        (await context.Set<AdmissionDecision>().CountAsync()).Should().Be(1);
        var current = await service.GetOfferAsync(seed.Application.PublicId);
        current.Success.Should().BeTrue();
        current.Data!.State.Should().Be(AdmissionDecisionState.Offered);
        var premature = await service.AdmitAsync(seed.Application.PublicId, Request(seed));
        premature.StatusCode.Should().Be(409);
        (await context.Set<Student>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Offer_issuance_rejects_stale_applicant_version_and_foreign_batch()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        context.RemoveRange(context.Set<AdmissionDecision>());
        await context.SaveChangesAsync();
        var service = CreateService(context, new TestCurrentUser(101));
        var stale = OfferRequest(seed);
        stale.ApplicantRowVersion = Convert.ToBase64String(new byte[8]);
        (await service.IssueOfferAsync(seed.Application.PublicId, stale)).StatusCode.Should().Be(409);
        var foreignBatch = OfferRequest(seed);
        foreignBatch.OfferedAcademicBatchId = seed.Batch.Id + 10000;
        (await service.IssueOfferAsync(seed.Application.PublicId, foreignBatch)).StatusCode.Should().Be(409);
        (await context.Set<AdmissionDecision>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Offer_issuance_enforces_tenant_and_role_boundaries()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        context.RemoveRange(context.Set<AdmissionDecision>());
        await context.SaveChangesAsync();
        var unauthorized = CreateService(context, new TestCurrentUser(101, "Teacher"));
        (await unauthorized.IssueOfferAsync(seed.Application.PublicId, OfferRequest(seed))).StatusCode.Should().Be(403);
        var otherTenant = CreateService(context, new TestCurrentUser(202));
        (await otherTenant.IssueOfferAsync(seed.Application.PublicId, OfferRequest(seed))).StatusCode.Should().Be(404);
        (await context.Set<AdmissionDecision>().CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task Signed_offer_acceptance_requires_verified_guardian_evidence_and_is_idempotent()
    {
        await using var context = CreateContext(101);
        var seed = await SeedAsync(context, 101, AdmissionApplicantState.Qualified);
        var offer = await context.Set<AdmissionDecision>().SingleAsync();
        offer.State = AdmissionDecisionState.Offered;
        offer.OfferedAt = Now.UtcDateTime.AddDays(-1);
        offer.ExpiresAt = Now.UtcDateTime.AddDays(7);
        offer.AcceptedAt = null;
        offer.RowVersion = Enumerable.Range(1, 8).Select(x => (byte)x).ToArray();
        await context.SaveChangesAsync();
        var doc = new AdmissionApplicantDocument
        {
            TenantId = 101, AdmissionApplicantId = seed.Application.Id, FileAssetId = 99,
            DocumentTypeCode = "OFFER_ACCEPTANCE", CreatedAt = Now.UtcDateTime,
            IsVerified = false, VerifiedAt = null
        };
        context.Add(doc);
        await context.SaveChangesAsync();
        var service = CreateService(context, new TestCurrentUser(101));
        var request = new VerifyAdmissionOfferAcceptanceRequestDto
        {
            AcceptanceDocumentId = doc.Id, DecisionRowVersion = Convert.ToBase64String(offer.RowVersion),
            SignerRole = "Guardian", IdentityChecked = true
        };
        (await service.RecordVerifiedAcceptanceAsync(seed.Application.PublicId, request)).StatusCode.Should().Be(409);
        doc.IsVerified = true; doc.VerifiedAt = Now.UtcDateTime; doc.VerifiedByUserId = 7;
        await context.SaveChangesAsync();
        request.SignerRole = "Applicant";
        (await service.RecordVerifiedAcceptanceAsync(seed.Application.PublicId, request)).StatusCode.Should().Be(409);
        request.SignerRole = "Guardian";
        (await service.RecordVerifiedAcceptanceAsync(seed.Application.PublicId, request)).Success.Should().BeTrue();
        (await service.RecordVerifiedAcceptanceAsync(seed.Application.PublicId, request)).Success.Should().BeTrue();
        (await context.Set<AdmissionDecision>().SingleAsync()).State.Should().Be(AdmissionDecisionState.Accepted);
    }

    private static MakeAdmissionDecisionRequestDto OfferRequest(SeedData seed) => new()
    {
        ClientRequestId = Guid.NewGuid(), AdmissionApplicantReference = seed.Application.PublicId,
        State = AdmissionDecisionState.Offered, OfferedAcademicBatchId = seed.Batch.Id,
        ExpiresAt = Now.UtcDateTime.AddDays(7),
        ApplicantRowVersion = Convert.ToBase64String(seed.Application.RowVersion)
    };

    private static AdmissionEnrollmentService CreateService(EduOSDbContext context, ICurrentUserService user) => new(
        new GenericRepository<AdmissionApplicant>(context),
        new GenericRepository<AdmissionIntakeForm>(context),
        new GenericRepository<AdmissionDecision>(context),
        new GenericRepository<AdmissionApplicantDocument>(context),
        new GenericRepository<AdmissionApplicantGuardian>(context),
        new GenericRepository<Student>(context),
        new GenericRepository<StudentEnrollment>(context),
        new GenericRepository<Guardian>(context),
        new GenericRepository<StudentGuardian>(context),
        new GenericRepository<Person>(context),
        new GenericRepository<StudentPersonLink>(context),
        new GenericRepository<AcademicBatch>(context),
        new GenericRepository<AcademicTrack>(context),
        new GenericRepository<AcademicCurriculum>(context),
        new GenericRepository<AcademicYear>(context),
        context, user,
        new FixedTimeProvider(Now), NullLogger<AdmissionEnrollmentService>.Instance, context);

    private static AdmitAdmissionApplicationDto Request(SeedData seed) => new()
    {
        AcademicBatchId = seed.Batch.Id, AcademicTrackId = null, Roll = "12",
        RowVersion = Convert.ToBase64String(seed.Application.RowVersion)
    };

    private static async Task<SeedData> SeedAsync(EduOSDbContext context, long tenant, AdmissionApplicantState state)
    {
        var year = new AcademicYear
        {
            TenantId = tenant, Code = "2026", Name = "2026", StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31), IsActive = true
        };
        var campus = new Campus { TenantId = tenant, Name = "Main", Code = "MAIN", IsActive = true };
        var program = new AcademicProgram { TenantId = tenant, Name = "Primary", Code = "PRI", IsActive = true };
        context.AddRange(year, campus, program);
        await context.SaveChangesAsync();
        var level = new AcademicLevel
        {
            TenantId = tenant, AcademicProgramId = program.Id, Name = "Class Six", Code = "SIX", LevelNo = 6, IsActive = true
        };
        var curriculum = new AcademicCurriculum
        {
            TenantId = tenant, AcademicProgramId = program.Id, Name = "Primary 2026", Code = "PRI26",
            EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true, IsActive = true
        };
        context.AddRange(level, curriculum);
        await context.SaveChangesAsync();
        var batch = new AcademicBatch
        {
            TenantId = tenant, CampusId = campus.Id, AcademicYearId = year.Id,
            AcademicProgramId = program.Id, AcademicLevelId = level.Id,
            Name = "Class Six - A", Code = "6-A", Capacity = 60, IsActive = true
        };
        var form = new AdmissionIntakeForm
        {
            TenantId = tenant, CampusId = campus.Id, AcademicYearId = year.Id,
            AcademicProgramId = program.Id, AcademicLevelId = level.Id,
            Title = "Primary admissions", Code = "ADM-2026", State = AdmissionFormState.Published
        };
        context.AddRange(batch, form);
        await context.SaveChangesAsync();
        var application = new AdmissionApplicant
        {
            TenantId = tenant, AdmissionIntakeFormId = form.Id,
            ClientRequestId = Guid.NewGuid(), ApplicationNumber = "A-2026-001",
            FullName = "Rahim Uddin", FullNameBangla = "রহিম উদ্দিন",
            DateOfBirth = new DateOnly(2012, 2, 3), Gender = "Male",
            Phone = "+8801712345678", State = state,
            RowVersion = Enumerable.Range(1, 8).Select(x => (byte)x).ToArray()
        };
        context.Add(application);
        await context.SaveChangesAsync();
        var acceptedOffer = new AdmissionDecision
        {
            TenantId = tenant, AdmissionApplicantId = application.Id, ClientRequestId = Guid.NewGuid(),
            OfferedAcademicBatchId = batch.Id, State = AdmissionDecisionState.Accepted, AcceptedAt = Now.UtcDateTime
        };
        var parent = new AdmissionApplicantGuardian
        {
            TenantId = tenant, AdmissionApplicantId = application.Id,
            FullName = "Karim Uddin", RelationCode = "Father", Phone = "+8801700000000", IsPrimary = true
        };
        context.AddRange(acceptedOffer, parent);
        await context.SaveChangesAsync();
        return new SeedData(application, batch);
    }

    private static EduOSDbContext CreateContext(long tenantId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
        httpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("admission-enrollment-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed record SeedData(AdmissionApplicant Application, AcademicBatch Batch);
    private sealed class FixedTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class TestCurrentUser(long tenant, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Admission User";
        public string? Email => "admission@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string requestedRole) => requestedRole == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
