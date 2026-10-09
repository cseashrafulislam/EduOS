using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Persistence;

public class TenantIsolationTests
{
    [Fact]
    public async Task Tenant_filter_is_parameterized_and_isolates_each_request()
    {
        var options = CreateOptions();

        // Build and seed the model without a tenant first. This guards against a
        // model-cache bug where the first request could permanently define filtering.
        await using (var seed = CreateContext(options))
        {
            seed.Set<AcademicLevel>().AddRange(
                new AcademicLevel { Name = "Tenant 101", Code = "T101", LevelNo = 1, AcademicProgramId = 1, TenantId = 101 },
                new AcademicLevel { Name = "Tenant 202", Code = "T202", LevelNo = 1, AcademicProgramId = 1, TenantId = 202 },
                new AcademicLevel
                {
                    Name = "Deleted tenant 101",
                    Code = "DEL", LevelNo = 2, AcademicProgramId = 1,
                    TenantId = 101,
                    IsDeleted = true
                });

            seed.TenantSettings.AddRange(
                new TenantSetting
                {
                    TenantId = 101,
                    Category = "Branding",
                    Key = "Name",
                    Value = "Tenant 101"
                },
                new TenantSetting
                {
                    TenantId = 202,
                    Category = "Branding",
                    Key = "Name",
                    Value = "Tenant 202"
                });

            await seed.SaveChangesAsync();
        }

        await using (var tenant101 = CreateContext(options, 101))
        {
            var classes = await tenant101.Set<AcademicLevel>().Select(x => x.Name).ToListAsync();
            classes.Should().Equal("Tenant 101");

            var settings = await tenant101.TenantSettings
                .Select(x => x.Value)
                .ToListAsync();
            settings.Should().Equal("Tenant 101");
        }

        await using (var tenant202 = CreateContext(options, 202))
        {
            var classes = await tenant202.Set<AcademicLevel>().Select(x => x.Name).ToListAsync();
            classes.Should().Equal("Tenant 202");
        }

        await using (var noTenant = CreateContext(options))
        {
            (await noTenant.Set<AcademicLevel>().CountAsync()).Should().Be(0);
            (await noTenant.TenantSettings.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task Stale_tenant_claim_without_canonical_context_cannot_select_tenant_data()
    {
        var options = CreateOptions();

        await using (var seed = CreateContext(options))
        {
            seed.Set<AcademicLevel>().Add(new AcademicLevel { Name = "Private", Code = "L1", LevelNo = 1, AcademicProgramId = 1, TenantId = 101 });
            await seed.SaveChangesAsync();
        }

        await using var staleClaim = CreateContextWithClaimOnly(options, 101);

        (await staleClaim.Set<AcademicLevel>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Generic_repository_cannot_read_another_tenants_record()
    {
        var options = CreateOptions();
        long otherTenantClassId;

        await using (var seed = CreateContext(options))
        {
            var item = new AcademicLevel { Name = "Private", Code = "L1", LevelNo = 1, AcademicProgramId = 1, TenantId = 202 };
            seed.Set<AcademicLevel>().Add(item);
            await seed.SaveChangesAsync();
            otherTenantClassId = item.Id;
        }

        await using var tenant101 = CreateContext(options, 101);
        var repository = new GenericRepository<AcademicLevel>(tenant101);

        var result = await repository.GetByIdAsync(otherTenantClassId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Authenticated_tenant_cannot_write_to_another_tenant()
    {
        var options = CreateOptions();
        await using var tenant101 = CreateContext(options, 101);

        tenant101.Set<AcademicLevel>().Add(
            new AcademicLevel { Name = "Wrong tenant", Code = "L1", LevelNo = 1, AcademicProgramId = 1, TenantId = 202 });

        var action = () => tenant101.SaveChangesAsync();

        await action.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Tenant boundary violation*");
    }

    [Fact]
    public async Task New_record_inherits_authenticated_tenant_when_not_supplied()
    {
        var options = CreateOptions();
        await using var tenant101 = CreateContext(options, 101);
        var item = new AcademicLevel { Name = "Current tenant", Code = "L1", LevelNo = 1, AcademicProgramId = 1 };

        tenant101.Set<AcademicLevel>().Add(item);
        await tenant101.SaveChangesAsync();

        item.TenantId.Should().Be(101);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"tenant-isolation-{Guid.NewGuid():N}")
            .Options;
    }

    private static EduOSDbContext CreateContext(
        DbContextOptions<EduOSDbContext> options,
        long? tenantId = null)
    {
        var httpContext = new DefaultHttpContext();

        if (tenantId.HasValue)
        {
            httpContext.User = CreatePrincipal(tenantId.Value);
            httpContext.Items["TenantId"] = tenantId.Value;
        }

        return new EduOSDbContext(
            options,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private static EduOSDbContext CreateContextWithClaimOnly(
        DbContextOptions<EduOSDbContext> options,
        long tenantId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = CreatePrincipal(tenantId)
        };

        return new EduOSDbContext(
            options,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private static ClaimsPrincipal CreatePrincipal(long tenantId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "9001"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication"));
}
