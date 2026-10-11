using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class SqlServerAcademicSetupIntegrationTests
{
    [Fact]
    public async Task Curriculum_activation_changes_one_current_version_atomically_and_preserves_old_rows()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var opts = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, curriculum1, curriculum2;
        await using (var setup = new EduOSDbContext(opts))
        {
            var tenant = new Tenant { Code = "CI-AC-" + suffix, Name = "Academic CI",
                Email = "ac-" + suffix + "@example.test", State = TenantState.Active };
            setup.Tenants.Add(tenant);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var program = new AcademicProgram { TenantId = tenantId,
                    Name = "Secondary", Code = "SEC" };
                setup.AcademicPrograms.Add(program);
                await setup.SaveChangesAsync();
                var old = new AcademicCurriculum { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "2025", Code = "SEC-2025", VersionNo = 1,
                    EffectiveFrom = new DateOnly(2025, 1, 1), IsCurrent = true };
                var next = new AcademicCurriculum { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "2026", Code = "SEC-2026", VersionNo = 2,
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = false };
                setup.AcademicCurriculums.AddRange(old, next);
                await setup.SaveChangesAsync();
                curriculum1 = old.Id; curriculum2 = next.Id;
            }
        }
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        await using var db = new EduOSDbContext(opts, accessor);
        var sut = new AcademicSetupService(
            new GenericRepository<AcademicProgram>(db), new GenericRepository<AcademicLevel>(db),
            new GenericRepository<Subject>(db), new GenericRepository<AcademicCurriculum>(db),
            new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicBatch>(db),
            new GenericRepository<Room>(db), new GenericRepository<ProgramCampus>(db),
            new GenericRepository<Campus>(db), new GenericRepository<AcademicDepartment>(db),
            new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicTerm>(db),
            new GenericRepository<AcademicTrack>(db), new GenericRepository<Medium>(db),
            new GenericRepository<Shift>(db), db, new AcademicAdmin(tenantId),
            NullLogger<AcademicSetupService>.Instance);
        var target = await db.AcademicCurriculums.AsNoTracking().SingleAsync(x => x.Id == curriculum2);
        Assert.Equal(8, target.RowVersion.Length);
        var stale = await sut.ActivateCurriculumAsync(curriculum2, Convert.ToBase64String(new byte[8]));
        Assert.Equal(409, stale.StatusCode);
        var updated = await sut.ActivateCurriculumAsync(curriculum2, Convert.ToBase64String(target.RowVersion));
        Assert.True(updated.Success, updated.Message);
        db.ChangeTracker.Clear();
        var rows = await db.AcademicCurriculums.AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, x => x.IsCurrent && x.Id == curriculum2);
        Assert.False(rows.Single(x => x.Id == curriculum1).IsCurrent);
        Assert.Equal(new DateOnly(2025, 1, 1), rows.Single(x => x.Id == curriculum1).EffectiveFrom);
    }
    [Fact]
    public async Task Academic_current_year_and_term_switches_are_atomic_on_real_sql_server()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var opts = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId;
        await using (var seed = new EduOSDbContext(opts))
        {
            var tenant = new Tenant { Code = "CI-FND-" + suffix, Name = "Foundation CI",
                Email = "fnd-" + suffix + "@example.test", State = TenantState.Active };
            seed.Tenants.Add(tenant);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
        }
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        await using var db = new EduOSDbContext(opts, accessor);
        var sut = new InstitutionFoundationService(
            new GenericRepository<Tenant>(db), new GenericRepository<Campus>(db),
            new GenericRepository<TenantSetting>(db), new GenericRepository<TenantMembership>(db),
            new GenericRepository<UserCampusAccess>(db), new GenericRepository<AcademicYear>(db),
            new GenericRepository<AcademicTerm>(db), new GenericRepository<AcademicBatch>(db),
            new GenericRepository<AdmissionIntakeForm>(db),
            new GenericRepository<TenantSubscription>(db), new GenericRepository<SubscriptionPlan>(db),
            db, new AcademicAdmin(tenantId), TimeProvider.System,
            NullLogger<InstitutionFoundationService>.Instance);
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);
        var a = await sut.SaveAcademicYearAsync(null, new SaveAcademicYearRequestDto
        {
            Name = "2026 Year A", Code = "YA-" + suffix, StartDate = start, EndDate = end,
            IsActive = true, IsCurrent = true
        });
        Assert.True(a.Success, a.Message);
        var b = await sut.SaveAcademicYearAsync(null, new SaveAcademicYearRequestDto
        {
            Name = "2026 Year B", Code = "YB-" + suffix, StartDate = start, EndDate = end,
            IsActive = true, IsCurrent = true
        });
        Assert.True(b.Success, b.Message);
        var invalid = await sut.SaveAcademicYearAsync(null, new SaveAcademicYearRequestDto
        {
            Name = "Invalid current", Code = "BAD-" + suffix, StartDate = start, EndDate = end,
            IsActive = false, IsCurrent = true
        });
        Assert.Equal(400, invalid.StatusCode);
        var duplicate = await sut.SaveAcademicYearAsync(null, new SaveAcademicYearRequestDto
        {
            Name = "Duplicate code", Code = "YA-" + suffix, StartDate = start, EndDate = end,
            IsActive = true
        });
        Assert.Equal(409, duplicate.StatusCode);

        var firstTerm = await sut.SaveAcademicTermAsync(null, new SaveAcademicTermRequestDto
        {
            AcademicYearId = b.Data!.Id, Name = "First", Code = "T1-" + suffix,
            StartDate = start, EndDate = new DateOnly(2026, 6, 30), IsCurrent = true
        });
        Assert.True(firstTerm.Success, firstTerm.Message);
        var secondTerm = await sut.SaveAcademicTermAsync(null, new SaveAcademicTermRequestDto
        {
            AcademicYearId = b.Data.Id, Name = "Second", Code = "T2-" + suffix,
            StartDate = new DateOnly(2026, 7, 1), EndDate = end, IsCurrent = true
        });
        Assert.True(secondTerm.Success, secondTerm.Message);
        db.ChangeTracker.Clear();
        var years = await db.AcademicYears.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
        var terms = await db.AcademicTerms.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
        Assert.Equal(2, years.Count);
        Assert.Single(years, x => x.Id == b.Data.Id && x.IsCurrent);
        Assert.False(years.Single(x => x.Id == a.Data!.Id).IsCurrent);
        Assert.Equal(2, terms.Count);
        Assert.Single(terms, x => x.Id == secondTerm.Data!.Id && x.IsCurrent);
        Assert.False(terms.Single(x => x.Id == firstTerm.Data!.Id).IsCurrent);
    }

    private sealed class AcademicAdmin(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenant;
        public string? FullName => "Academic CI";
        public string? Email => "test@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL Server CI";
    }
}
