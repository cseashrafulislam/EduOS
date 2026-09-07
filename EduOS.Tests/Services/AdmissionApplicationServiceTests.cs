using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class AdmissionApplicationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_normalizes_bangla_mobile_and_owns_record_by_current_tenant()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var references = await SeedReferencesAsync(context, 101);
        var service = CreateService(context, new TestCurrentUser(101));

        var result = await service.CreateAsync(CreateRequest(references, "০১৭১২-৩৪৫৬৭৮"));

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.ApplicationNumber.Should().StartWith("APP-2026-");
        var saved = await context.AdmissionApplicants.SingleAsync();
        saved.TenantId.Should().Be(101);
        saved.PrimaryMobile.Should().Be("+8801712345678");
        saved.Status.Should().Be(AdmissionApplicationStatus.Submitted);
        var audit = await context.AuditLogs.SingleAsync(x => x.TableName == nameof(AdmissionApplicant));
        audit.NewValue.Should().BeNull("applicant PII must not be copied into the general audit payload");
        typeof(AdmissionApplicant).GetProperties().Select(x => x.Name)
            .Should().NotContain(x => x.Contains("Nid", StringComparison.OrdinalIgnoreCase)
                                      || x.Contains("BirthCert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Repeated_client_request_is_idempotent_but_changed_payload_is_rejected()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var references = await SeedReferencesAsync(context, 101);
        var service = CreateService(context, new TestCurrentUser(101));
        var request = CreateRequest(references);

        var first = await service.CreateAsync(request);
        var replay = await service.CreateAsync(request);
        request.ApplicantName = "Different Applicant";
        var conflict = await service.CreateAsync(request);

        first.StatusCode.Should().Be(201);
        replay.Success.Should().BeTrue();
        replay.Data!.Reference.Should().Be(first.Data!.Reference);
        conflict.StatusCode.Should().Be(409);
        (await context.AdmissionApplicants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Minor_requires_complete_guardian_contact()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var references = await SeedReferencesAsync(context, 101);
        var service = CreateService(context, new TestCurrentUser(101));
        var request = CreateRequest(references);
        request.GuardianMobile = null;

        var result = await service.CreateAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        (await context.AdmissionApplicants.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Foreign_tenant_references_and_records_are_not_visible()
    {
        var options = CreateOptions();
        Guid privateReference;
        await using (var tenant202 = CreateContext(options, 202))
        {
            var references = await SeedReferencesAsync(tenant202, 202);
            var created = await CreateService(tenant202, new TestCurrentUser(202)).CreateAsync(CreateRequest(references));
            privateReference = created.Data!.Reference;
        }

        await using var tenant101 = CreateContext(options, 101);
        var ownReferences = await SeedReferencesAsync(tenant101, 101);
        var service = CreateService(tenant101, new TestCurrentUser(101));
        var invalid = CreateRequest(ownReferences);
        invalid.CampusId = ownReferences.CampusId + 100000;

        var foreignDetails = await service.GetByReferenceAsync(privateReference);
        var list = await service.GetPageAsync(new AdmissionApplicationQueryDto());
        var invalidCreate = await service.CreateAsync(invalid);

        foreignDetails.StatusCode.Should().Be(404);
        list.Data!.Items.Should().BeEmpty();
        invalidCreate.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Review_enforces_transition_note_and_row_version()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var references = await SeedReferencesAsync(context, 101);
        var service = CreateService(context, new TestCurrentUser(101));
        var created = await service.CreateAsync(CreateRequest(references));

        var directApproval = await service.ReviewAsync(created.Data!.Reference, new ReviewAdmissionApplicationDto
        {
            Status = AdmissionApplicationStatus.Approved,
            RowVersion = created.Data.RowVersion
        });
        var stale = await service.ReviewAsync(created.Data.Reference, new ReviewAdmissionApplicationDto
        {
            Status = AdmissionApplicationStatus.UnderReview,
            RowVersion = Convert.ToBase64String([1])
        });
        var underReview = await service.ReviewAsync(created.Data.Reference, new ReviewAdmissionApplicationDto
        {
            Status = AdmissionApplicationStatus.UnderReview,
            RowVersion = created.Data.RowVersion
        });
        var noRejectionNote = await service.ReviewAsync(created.Data.Reference, new ReviewAdmissionApplicationDto
        {
            Status = AdmissionApplicationStatus.Rejected,
            RowVersion = underReview.Data!.RowVersion
        });

        directApproval.StatusCode.Should().Be(409);
        stale.StatusCode.Should().Be(409);
        underReview.Success.Should().BeTrue();
        noRejectionNote.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Non_admission_role_is_denied_before_data_access()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var service = CreateService(context, new TestCurrentUser(101, "Teacher"));

        var result = await service.GetPageAsync(new AdmissionApplicationQueryDto());

        result.StatusCode.Should().Be(403);
    }

    private static AdmissionApplicationService CreateService(EduOSDbContext context, ICurrentUserService user) => new(
        new GenericRepository<AdmissionApplicant>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<AcademicTerm>(context),
        new GenericRepository<Campus>(context),
        new GenericRepository<Class>(context),
        context,
        user,
        new FixedTimeProvider(Now),
        NullLogger<AdmissionApplicationService>.Instance);

    private static CreateAdmissionApplicationDto CreateRequest(ReferenceIds ids, string mobile = "01712345678") => new()
    {
        ClientRequestId = Guid.NewGuid(),
        AcademicYearId = ids.YearId,
        CampusId = ids.CampusId,
        AcademicUnitId = ids.UnitId,
        ApplicantName = "Rahim Uddin",
        ApplicantNameBangla = "রহিম উদ্দিন",
        DateOfBirth = new DateTime(2012, 2, 3),
        Gender = Gender.Male,
        PrimaryMobile = mobile,
        GuardianName = "Karim Uddin",
        GuardianRelation = "Father",
        GuardianMobile = "01700000000",
        PreferredLanguage = "bn-BD"
    };

    private static async Task<ReferenceIds> SeedReferencesAsync(EduOSDbContext context, long tenantId)
    {
        var year = new AcademicYear { TenantId = tenantId, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true };
        var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN", IsActive = true };
        var unit = new Class { TenantId = tenantId, Name = "Class Six", NumericValue = 6, IsActive = true };
        context.AddRange(year, campus, unit);
        await context.SaveChangesAsync();
        return new ReferenceIds(year.Id, campus.Id, unit.Id);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"admission-{Guid.NewGuid():N}")
        .Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
        httpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed record ReferenceIds(long YearId, long CampusId, long UnitId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestCurrentUser(long tenantId, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
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
