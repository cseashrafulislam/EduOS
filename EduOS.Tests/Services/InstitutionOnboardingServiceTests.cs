using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class InstitutionOnboardingServiceTests
{
    [Fact]
    public async Task New_campus_is_rejected_at_plan_limit_without_changing_the_limit()
    {
        await using var setup = await CreateSetupAsync(maxCampuses: 1);
        setup.Context.Campuses.Add(new Campus
        {
            TenantId = setup.Tenant.Id,
            Name = "Main Campus",
            Code = "MAIN",
            IsHeadOffice = true
        });
        await setup.Context.SaveChangesAsync();

        var result = await setup.Service.SaveCampusAsync(new CampusSetupDto
        {
            Name = "Second Campus",
            Code = "SECOND"
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        setup.Tenant.MaxCampuses.Should().Be(1);
        (await setup.Context.Campuses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task First_campus_becomes_head_office_and_preserves_plan_capacity()
    {
        await using var setup = await CreateSetupAsync(maxCampuses: 3);

        var result = await setup.Service.SaveCampusAsync(new CampusSetupDto
        {
            Name = "  Dhaka Campus  ",
            Code = " dhk "
        });

        result.Success.Should().BeTrue();
        var campus = await setup.Context.Campuses.SingleAsync();
        campus.Name.Should().Be("Dhaka Campus");
        campus.Code.Should().Be("DHK");
        campus.IsHeadOffice.Should().BeTrue();
        setup.Tenant.MaxCampuses.Should().Be(3);
    }

    [Fact]
    public async Task Deleting_head_office_promotes_the_oldest_remaining_campus()
    {
        await using var setup = await CreateSetupAsync(maxCampuses: 3);
        var headOffice = new Campus
        {
            TenantId = setup.Tenant.Id,
            Name = "Main Campus",
            Code = "MAIN",
            IsHeadOffice = true
        };
        var branch = new Campus
        {
            TenantId = setup.Tenant.Id,
            Name = "Branch Campus",
            Code = "BRANCH"
        };
        setup.Context.Campuses.AddRange(headOffice, branch);
        await setup.Context.SaveChangesAsync();

        var result = await setup.Service.DeleteCampusAsync(headOffice.Id);

        result.Success.Should().BeTrue();
        var remaining = await setup.Context.Campuses.SingleAsync();
        remaining.Id.Should().Be(branch.Id);
        remaining.IsHeadOffice.Should().BeTrue();
    }

    [Fact]
    public async Task Term_dates_outside_the_selected_academic_year_are_rejected()
    {
        await using var setup = await CreateSetupAsync();
        var year = new AcademicYear
        {
            TenantId = setup.Tenant.Id,
            Name = "2026",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsCurrent = true
        };
        setup.Context.AcademicYears.Add(year);
        await setup.Context.SaveChangesAsync();

        var result = await setup.Service.SaveAcademicTermAsync(new AcademicTermSetupDto
        {
            AcademicYearId = year.Id,
            Name = "Winter Term",
            StartDate = new DateTime(2025, 12, 15),
            EndDate = new DateTime(2026, 3, 31)
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        (await setup.Context.AcademicTerms.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Academic_year_cannot_exclude_an_existing_term()
    {
        await using var setup = await CreateSetupAsync();
        var year = new AcademicYear
        {
            TenantId = setup.Tenant.Id,
            Name = "2026",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsCurrent = true
        };
        setup.Context.AcademicYears.Add(year);
        await setup.Context.SaveChangesAsync();
        setup.Context.AcademicTerms.Add(new AcademicTerm
        {
            TenantId = setup.Tenant.Id,
            AcademicYearId = year.Id,
            Name = "Term One",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 4, 30)
        });
        await setup.Context.SaveChangesAsync();

        var result = await setup.Service.SaveAcademicYearAsync(new AcademicYearSetupDto
        {
            Id = year.Id,
            Name = year.Name,
            StartDate = new DateTime(2026, 2, 1),
            EndDate = year.EndDate,
            IsCurrent = true
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        year.StartDate.Should().Be(new DateTime(2026, 1, 1));
    }

    private static async Task<TestSetup> CreateSetupAsync(int maxCampuses = 3)
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"institution-onboarding-{Guid.NewGuid():N}")
            .Options;
        var context = new EduOSDbContext(options, accessor);
        var tenant = new Tenant
        {
            Name = "Test Institution",
            Code = $"INST-{Guid.NewGuid():N}"[..20],
            Email = "institution@example.test",
            OwnerName = "Tenant Owner",
            MaxCampuses = maxCampuses
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        httpContext.Items["TenantId"] = tenant.Id;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.Id.ToString())
        ], "TestAuthentication"));

        var currentUser = new TestCurrentUser(tenant.Id);
        var service = new InstitutionOnboardingService(
            CreateUserManager(),
            new GenericRepository<Tenant>(context),
            new GenericRepository<Campus>(context),
            new GenericRepository<AcademicYear>(context),
            new GenericRepository<AcademicTerm>(context),
            new GenericRepository<InstitutionTypeDefinition>(context),
            Mock.Of<ITenantModuleService>(),
            context,
            currentUser,
            NullLogger<InstitutionOnboardingService>.Instance);

        return new TestSetup(context, tenant, service);
    }

    private static UserManager<ApplicationUser> CreateUserManager()
    {
        var store = Mock.Of<IUserStore<ApplicationUser>>();
        return new UserManager<ApplicationUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private sealed record TestSetup(
        EduOSDbContext Context,
        Tenant Tenant,
        InstitutionOnboardingService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 99;
        public long TenantId => tenantId;
        public string? FullName => "Tenant Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
