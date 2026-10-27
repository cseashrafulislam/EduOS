using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InstitutionFoundationReferenceIntegrityTests
{
    [Theory]
    [InlineData("enrollment")]
    [InlineData("offering")]
    [InlineData("calendar")]
    public async Task Academic_year_cannot_be_archived_with_direct_operational_history(string dependency)
    {
        await using var scope = await SetupAsync(false);
        await AddReferenceAsync(scope.Db, scope.Year.Id, null, dependency);
        var result = await scope.Service.ArchiveAcademicYearAsync(scope.Year.Id, Version(scope.Year.RowVersion));
        Assert.Equal(409, result.StatusCode);
        Assert.False(scope.Year.IsDeleted);
    }

    [Theory]
    [InlineData("enrollment")]
    [InlineData("offering")]
    [InlineData("calendar")]
    public async Task Academic_term_cannot_be_archived_with_direct_operational_history(string dependency)
    {
        await using var scope = await SetupAsync(true);
        await AddReferenceAsync(scope.Db, scope.Year.Id, scope.Term!.Id, dependency);
        var result = await scope.Service.ArchiveAcademicTermAsync(scope.Term.Id, Version(scope.Term.RowVersion));
        Assert.Equal(409, result.StatusCode);
        Assert.False(scope.Term.IsDeleted);
    }

    [Theory]
    [InlineData("enrollment")]
    [InlineData("offering")]
    [InlineData("calendar")]
    public async Task Academic_year_campus_cannot_be_reassigned_after_operational_use(string dependency)
    {
        await using var scope = await SetupAsync(false);
        await AddReferenceAsync(scope.Db, scope.Year.Id, null, dependency);
        var result = await scope.Service.SaveAcademicYearAsync(scope.Year.Id,
            YearRequest(scope.Year, scope.South.Id));
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(scope.North.Id, scope.Year.CampusId);
    }

    [Theory]
    [InlineData("enrollment")]
    [InlineData("offering")]
    [InlineData("calendar")]
    public async Task Academic_term_cannot_move_to_another_year_after_operational_use(string dependency)
    {
        await using var scope = await SetupAsync(true);
        await AddReferenceAsync(scope.Db, scope.Year.Id, scope.Term!.Id, dependency);
        var result = await scope.Service.SaveAcademicTermAsync(scope.Term.Id,
            TermRequest(scope.Term, scope.OtherYear.Id));
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(scope.Year.Id, scope.Term.AcademicYearId);
    }

    [Fact]
    public async Task Academic_year_cannot_shrink_past_calendar_events()
    {
        await using var scope = await SetupAsync(false);
        await AddReferenceAsync(scope.Db, scope.Year.Id, null, "calendar");
        var request = YearRequest(scope.Year, scope.North.Id);
        request.StartDate = new DateOnly(2026, 2, 1);
        var result = await scope.Service.SaveAcademicYearAsync(scope.Year.Id, request);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(new DateOnly(2026, 1, 1), scope.Year.StartDate);
    }

    [Fact]
    public async Task Academic_term_cannot_shrink_past_calendar_events()
    {
        await using var scope = await SetupAsync(true);
        await AddReferenceAsync(scope.Db, scope.Year.Id, scope.Term!.Id, "calendar");
        var request = TermRequest(scope.Term, scope.Year.Id);
        request.StartDate = new DateOnly(2026, 2, 1);
        var result = await scope.Service.SaveAcademicTermAsync(scope.Term.Id, request);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(new DateOnly(2026, 1, 1), scope.Term.StartDate);
    }

    [Fact]
    public async Task Academic_year_cannot_shrink_past_enrollment_dates()
    {
        await using var scope = await SetupAsync(false);
        await AddReferenceAsync(scope.Db, scope.Year.Id, null, "enrollment");
        var request = YearRequest(scope.Year, scope.North.Id);
        request.StartDate = new DateOnly(2026, 4, 1);
        var result = await scope.Service.SaveAcademicYearAsync(scope.Year.Id, request);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(new DateOnly(2026, 1, 1), scope.Year.StartDate);
    }

    [Fact]
    public async Task Multi_year_enrollment_end_does_not_block_ordinary_year_metadata_edit()
    {
        await using var scope = await SetupAsync(false);
        await AddReferenceAsync(scope.Db, scope.Year.Id, null, "enrollment");
        var enrollment = await scope.Db.StudentEnrollments.SingleAsync();
        enrollment.EndDate = new DateOnly(2027, 6, 30);
        await scope.Db.SaveChangesAsync();
        var request = YearRequest(scope.Year, scope.North.Id);
        request.Name = "Academic year 2026";
        var result = await scope.Service.SaveAcademicYearAsync(scope.Year.Id, request);
        Assert.True(result.Success, result.Message);
        Assert.Equal("Academic year 2026", scope.Year.Name);
    }

    [Fact]
    public async Task Unreferenced_academic_year_can_change_campus()
    {
        await using var scope = await SetupAsync(false);
        var result = await scope.Service.SaveAcademicYearAsync(scope.Year.Id,
            YearRequest(scope.Year, scope.South.Id));
        Assert.True(result.Success, result.Message);
        Assert.Equal(scope.South.Id, scope.Year.CampusId);
    }

    [Fact]
    public async Task Unreferenced_academic_term_can_move_to_another_year()
    {
        await using var scope = await SetupAsync(true);
        var result = await scope.Service.SaveAcademicTermAsync(scope.Term!.Id,
            TermRequest(scope.Term, scope.OtherYear.Id));
        Assert.True(result.Success, result.Message);
        Assert.Equal(scope.OtherYear.Id, scope.Term.AcademicYearId);
    }

    private static SaveAcademicYearRequestDto YearRequest(AcademicYear row, long campusId) => new()
    {
        Name = row.Name, Code = row.Code, CampusId = campusId, CycleType = row.CycleType,
        StartDate = row.StartDate, EndDate = row.EndDate, IsCurrent = row.IsCurrent,
        IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };

    private static SaveAcademicTermRequestDto TermRequest(AcademicTerm row, long yearId) => new()
    {
        AcademicYearId = yearId, Name = row.Name, Code = row.Code,
        StartDate = row.StartDate, EndDate = row.EndDate, IsCurrent = row.IsCurrent,
        IsActive = row.IsActive, DisplayOrder = row.DisplayOrder, RowVersion = Version(row.RowVersion)
    };

    private static string Version(byte[] version) => Convert.ToBase64String(version);

    private static async Task AddReferenceAsync(EduOSDbContext db, long yearId, long? termId, string kind)
    {
        switch (kind)
        {
            case "enrollment":
                db.StudentEnrollments.Add(new StudentEnrollment
                {
                    TenantId = 101, StudentId = 1, CampusId = 1,
                    AcademicYearId = yearId, AcademicTermId = termId,
                    AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1,
                    AcademicCurriculumId = 1, RollNo = "R-1",
                    EnrollmentDate = new DateOnly(2026, 3, 1)
                });
                break;
            case "offering":
                db.SubjectOfferings.Add(new SubjectOffering
                {
                    TenantId = 101, AcademicYearId = yearId, AcademicTermId = termId,
                    AcademicBatchId = 1, CurriculumSubjectId = 1, Code = "O-1"
                });
                break;
            case "calendar":
                db.AcademicCalendarEvents.Add(new AcademicCalendarEvent
                {
                    TenantId = 101, AcademicYearId = yearId, AcademicTermId = termId,
                    Title = "Opening", StartDate = new DateOnly(2026, 1, 10),
                    EndDate = new DateOnly(2026, 1, 20)
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        await db.SaveChangesAsync();
    }

    private static async Task<Scope> SetupAsync(bool includeTerm)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "101")
        }, "TestAuthentication"));
        var accessor = new HttpContextAccessor { HttpContext = http };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("foundation-references-" + Guid.NewGuid().ToString("N")).Options;
        var db = new EduOSDbContext(options, accessor);
        var north = new Campus { TenantId = 101, Name = "North", Code = "N", IsActive = true };
        var south = new Campus { TenantId = 101, Name = "South", Code = "S", IsActive = true };
        db.Campuses.AddRange(north, south);
        db.Tenants.Add(new Tenant { Id = 101, Name = "Institution", Code = "T101",
            Email = "admin@example.test", InstitutionTypeDefinitionId = 1 });
        await db.SaveChangesAsync();
        var year = new AcademicYear
        {
            TenantId = 101, CampusId = north.Id, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            IsActive = true, RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        };
        var otherYear = new AcademicYear
        {
            TenantId = 101, CampusId = north.Id, Name = "Other 2026", Code = "2026-ALT",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            IsActive = true, RowVersion = new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }
        };
        db.AcademicYears.AddRange(year, otherYear);
        await db.SaveChangesAsync();
        AcademicTerm? term = null;
        if (includeTerm)
        {
            term = new AcademicTerm
            {
                TenantId = 101, AcademicYearId = year.Id, Name = "Term 1",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 30),
                IsActive = true, RowVersion = new byte[] { 2, 3, 4, 5, 6, 7, 8, 9 }
            };
            db.AcademicTerms.Add(term);
            await db.SaveChangesAsync();
        }
        var user = new CurrentUser();
        var service = new InstitutionFoundationService(
            new GenericRepository<Tenant>(db), new GenericRepository<Campus>(db),
            new GenericRepository<TenantSetting>(db), new GenericRepository<TenantMembership>(db),
            new GenericRepository<UserCampusAccess>(db), new GenericRepository<AcademicYear>(db),
            new GenericRepository<AcademicTerm>(db), new GenericRepository<AcademicBatch>(db),
            new GenericRepository<AdmissionIntakeForm>(db), new GenericRepository<TenantSubscription>(db),
            new GenericRepository<SubscriptionPlan>(db), db, db, user, TimeProvider.System,
            NullLogger<InstitutionFoundationService>.Instance);
        return new Scope(db, service, year, otherYear, term, north, south);
    }

    private sealed record Scope(EduOSDbContext Db, InstitutionFoundationService Service,
        AcademicYear Year, AcademicYear OtherYear, AcademicTerm? Term, Campus North, Campus South) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class CurrentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => new[] { "TenantAdmin" };
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
