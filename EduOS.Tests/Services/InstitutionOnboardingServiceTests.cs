using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InstitutionOnboardingServiceTests
{
    [Fact]
    public async Task First_campus_is_head_office_and_default_subscription_limit_is_enforced()
    {
        var options = Options();
        await SeedTenant(options, 101);
        await using var db = Context(options, 101);
        var service = Foundation(db, new TestUser(101));
        var first = await service.SaveCampusAsync(null, new SaveCampusRequestDto
        {
            Name = "Dhaka Campus", Code = "DHK", HeadName = "Principal",
            IsHeadOffice = false
        });
        var second = await service.SaveCampusAsync(null, new SaveCampusRequestDto
        {
            Name = "Chattogram Campus", Code = "CTG"
        });
        first.Success.Should().BeTrue();
        first.Data!.IsHeadOffice.Should().BeTrue();
        first.Data.HeadName.Should().Be("Principal");
        second.StatusCode.Should().Be(409);
        (await db.Campuses.CountAsync()).Should().Be(1);
        var listed = await service.GetCampusesAsync();
        listed.Data.Should().ContainSingle();
        listed.Data![0].HeadName.Should().Be("Principal");
    }

    [Fact]
    public async Task Academic_term_cannot_start_outside_academic_year()
    {
        var options = Options();
        await SeedTenant(options, 101);
        await using var db = Context(options, 101);
        var service = Foundation(db, new TestUser(101));
        var year = await service.SaveAcademicYearAsync(null, new SaveAcademicYearRequestDto
        {
            Name = "Academic 2026", Code = "Y2026",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31), IsCurrent = true
        });
        year.Success.Should().BeTrue();
        var invalid = await service.SaveAcademicTermAsync(null, new SaveAcademicTermRequestDto
        {
            AcademicYearId = year.Data!.Id, Name = "Invalid term",
            StartDate = new DateOnly(2025, 12, 15),
            EndDate = new DateOnly(2026, 3, 15)
        });
        var valid = await service.SaveAcademicTermAsync(null, new SaveAcademicTermRequestDto
        {
            AcademicYearId = year.Data.Id, Name = "First term",
            StartDate = new DateOnly(2026, 1, 5),
            EndDate = new DateOnly(2026, 4, 30), IsCurrent = true
        });
        invalid.StatusCode.Should().Be(409);
        valid.Success.Should().BeTrue();
        (await db.AcademicTerms.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Foundation_setup_rejects_foreign_tenant_records()
    {
        var options = Options();
        await SeedTenant(options, 101);
        await SeedTenant(options, 202);
        await using (var foreign = Context(options, 202))
        {
            foreign.Campuses.Add(new Campus
            {
                TenantId = 202, Name = "Foreign Campus", Code = "FC",
                IsHeadOffice = true
            });
            await foreign.SaveChangesAsync();
        }
        await using var own = Context(options, 101);
        var service = Foundation(own, new TestUser(101));
        (await service.GetCampusesAsync()).Data.Should().BeEmpty();
        (await service.GetCampusAsync(1)).StatusCode.Should().Be(404);
    }

    [Fact]
    public void Canonical_institution_registration_and_foundation_contracts_are_registered()
    {
        typeof(IInstitutionRegistrationService).IsAssignableFrom(typeof(InstitutionOnboardingService))
            .Should().BeTrue();
        typeof(IInstitutionFoundationService).IsAssignableFrom(typeof(InstitutionFoundationService))
            .Should().BeTrue();
        typeof(IInstitutionProfileWizardService).IsAssignableFrom(typeof(InstitutionProfileWizardService))
            .Should().BeTrue();
        typeof(IInstitutionFoundationService).GetMethods().Should().HaveCount(12);
    }

    private static InstitutionFoundationService Foundation(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Tenant>(db), new GenericRepository<Campus>(db),
        new GenericRepository<TenantSetting>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicTerm>(db),
        new GenericRepository<AcademicBatch>(db), new GenericRepository<AdmissionIntakeForm>(db),
        new GenericRepository<TenantSubscription>(db), new GenericRepository<SubscriptionPlan>(db),
        db, user, TimeProvider.System, NullLogger<InstitutionFoundationService>.Instance);

    private static async Task SeedTenant(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        await using var db = Context(options, tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Code = "TN-" + tenantId, Name = "Institution",
            Email = "owner" + tenantId + "@example.test", State = EduOS.Core.Enums.Domain.TenantState.Active
        });
        await db.SaveChangesAsync();
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("institution-setup-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 99;
        public string? FullName => "Tenant owner";
        public string? Email => "owner@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
