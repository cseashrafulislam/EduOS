using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.DTOs.Tenants;
using System.ComponentModel.DataAnnotations;
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

public sealed class InstitutionRegistrationServiceTests
{
    [Fact]
    public void Campus_wizard_accepts_blank_code_but_canonical_request_still_requires_it()
    {
        var wizard = new CampusOnboardingRequestDto { Name = "Dhaka Campus" };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(wizard, new ValidationContext(wizard), errors, true).Should().BeTrue();
        var canonical = new SaveCampusRequestDto { Name = "Dhaka Campus", Code = string.Empty };
        errors.Clear();
        Validator.TryValidateObject(canonical, new ValidationContext(canonical), errors, true).Should().BeFalse();
    }

    [Fact]
    public async Task Campus_configuration_round_trip_and_legacy_update_preserves_values()
    {
        var options = Options();
        await SeedTenant(options, 101);
        await using var db = Context(options, 101);
        var service = Foundation(db, new TestUser(101));
        var created = await service.SaveCampusAsync(null, new SaveCampusRequestDto
        {
            Name = "Online Campus", Code = "ONL", IsOnlineCampus = true, TimeZoneId = "Asia/Dhaka"
        });
        created.Success.Should().BeTrue();
        created.Data!.IsOnlineCampus.Should().BeTrue();
        created.Data.TimeZoneId.Should().Be("Asia/Dhaka");
        // EF InMemory does not generate SQL Server rowversion values.
        var savedCampus = await db.Campuses.SingleAsync(x => x.Id == created.Data.Id);
        savedCampus.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();
        var legacy = await service.SaveCampusAsync(created.Data.Id, new SaveCampusRequestDto
        {
            Name = "Online Campus Updated", Code = "ONL", IsHeadOffice = true,
            RowVersion = Convert.ToBase64String(savedCampus.RowVersion)
        });
        legacy.Success.Should().BeTrue(legacy.Message);
        legacy.Data!.IsOnlineCampus.Should().BeTrue();
        legacy.Data.TimeZoneId.Should().Be("Asia/Dhaka");
        var cleared = await service.SaveCampusAsync(legacy.Data.Id, new SaveCampusRequestDto
        {
            Name = "Online Campus Updated", Code = "ONL", IsHeadOffice = true,
            RowVersion = legacy.Data.RowVersion, IsOnlineCampus = false, TimeZoneId = ""
        });
        cleared.Success.Should().BeTrue();
        cleared.Data!.IsOnlineCampus.Should().BeFalse();
        cleared.Data.TimeZoneId.Should().BeNull();
    }

    [Fact]
    public async Task Campus_invalid_timezone_is_rejected_before_persistence()
    {
        var options = Options();
        await SeedTenant(options, 101);
        await using var db = Context(options, 101);
        var service = Foundation(db, new TestUser(101));
        var rejected = await service.SaveCampusAsync(null, new SaveCampusRequestDto
        {
            Name = "Invalid Zone", Code = "BAD", TimeZoneId = "Not/A_Real_Zone"
        });
        rejected.Success.Should().BeFalse();
        (await db.Campuses.CountAsync()).Should().Be(0);
    }

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

    [Theory]
    [InlineData("route")]
    [InlineData("vehicle")]
    [InlineData("hostel")]
    [InlineData("enrollment")]
    [InlineData("fee")]
    [InlineData("inventory")]
    [InlineData("library")]
    public async Task Campus_archive_rejects_cross_module_references_without_changing_head_office(string domain)
    {
        var options = Options();
        await SeedTenant(options, 101);
        await using var db = Context(options, 101);
        db.Campuses.AddRange(
            new Campus { Id = 1001, TenantId = 101, Code = "A", Name = "Main", IsActive = true,
                IsHeadOffice = true, RowVersion = [1, 2, 3] },
            new Campus { Id = 1002, TenantId = 101, Code = "B", Name = "Other", IsActive = true,
                RowVersion = [4, 5, 6] });
        await db.SaveChangesAsync();
        switch (domain)
        {
            case "route": db.Routes.Add(new EduOS.Core.Entities.Transport.Route
                { TenantId = 101, CampusId = 1001, Code = "R1", Name = "Route" }); break;
            case "vehicle": db.Vehicles.Add(new EduOS.Core.Entities.Transport.Vehicle
                { TenantId = 101, CampusId = 1001, VehicleNumber = "V1", Capacity = 10 }); break;
            case "hostel": db.Hostels.Add(new EduOS.Core.Entities.Hostel.Hostel
                { TenantId = 101, CampusId = 1001, Code = "H1", Name = "Hostel" }); break;
            case "enrollment": db.StudentEnrollments.Add(new StudentEnrollment
                { TenantId = 101, CampusId = 1001, RollNo = "S1" }); break;
            case "fee": db.FeeStructures.Add(new EduOS.Core.Entities.Finance.FeeStructure
                { TenantId = 101, CampusId = 1001, Name = "Fee" }); break;
            case "inventory": db.InventoryLocations.Add(new EduOS.Core.Entities.Inventory.InventoryLocation
                { TenantId = 101, CampusId = 1001, Code = "L1", Name = "Store" }); break;
            case "library": db.LibraryBranches.Add(new EduOS.Core.Entities.Library.LibraryBranch
                { TenantId = 101, CampusId = 1001, Code = "LB1", Name = "Library" }); break;
        }
        await db.SaveChangesAsync();
        var result = await Foundation(db, new TestUser(101))
            .ArchiveCampusAsync(1001, Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("operational records");
        (await db.Campuses.SingleAsync(x => x.Id == 1001)).IsDeleted.Should().BeFalse();
        (await db.Campuses.SingleAsync(x => x.Id == 1001)).IsHeadOffice.Should().BeTrue();
        (await db.Campuses.SingleAsync(x => x.Id == 1002)).IsHeadOffice.Should().BeFalse();
    }

    [Fact]
    public void Canonical_institution_registration_and_foundation_contracts_are_registered()
    {
        typeof(IInstitutionRegistrationService).IsAssignableFrom(typeof(InstitutionRegistrationService))
            .Should().BeTrue();
        typeof(IInstitutionFoundationService).IsAssignableFrom(typeof(InstitutionFoundationService))
            .Should().BeTrue();
        typeof(ITenantOnboardingProfileService).IsAssignableFrom(typeof(TenantOnboardingProfileService))
            .Should().BeTrue();
        typeof(IInstitutionFoundationService).GetMethods().Should().HaveCount(12);
    }

    private static InstitutionFoundationService Foundation(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Tenant>(db), new GenericRepository<Campus>(db),
        new GenericRepository<TenantSetting>(db),
        new GenericRepository<EduOS.Core.Entities.Auth.TenantMembership>(db),
        new GenericRepository<EduOS.Core.Entities.Auth.UserCampusAccess>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicTerm>(db),
        new GenericRepository<AcademicBatch>(db), new GenericRepository<AdmissionIntakeForm>(db),
        new GenericRepository<TenantSubscription>(db), new GenericRepository<SubscriptionPlan>(db),
        db, db, user, TimeProvider.System, NullLogger<InstitutionFoundationService>.Instance);

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
