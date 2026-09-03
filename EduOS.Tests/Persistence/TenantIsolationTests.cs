using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Tenants;
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
            seed.Classes.AddRange(
                new Class { Name = "Tenant 101", NumericValue = 1, TenantId = 101 },
                new Class { Name = "Tenant 202", NumericValue = 1, TenantId = 202 },
                new Class
                {
                    Name = "Deleted tenant 101",
                    NumericValue = 2,
                    TenantId = 101,
                    IsDeleted = true
                });

            seed.TenantSettings.AddRange(
                new TenantSetting
                {
                    TenantId = 101,
                    Category = "Branding",
                    SettingKey = "Name",
                    SettingValue = "Tenant 101"
                },
                new TenantSetting
                {
                    TenantId = 202,
                    Category = "Branding",
                    SettingKey = "Name",
                    SettingValue = "Tenant 202"
                });

            await seed.SaveChangesAsync();
        }

        await using (var tenant101 = CreateContext(options, 101, "TenantId"))
        {
            var classes = await tenant101.Classes.Select(x => x.Name).ToListAsync();
            classes.Should().Equal("Tenant 101");

            var settings = await tenant101.TenantSettings
                .Select(x => x.SettingValue)
                .ToListAsync();
            settings.Should().Equal("Tenant 101");
        }

        // JWTs historically used the lower-camel claim. It must receive the same isolation.
        await using (var tenant202 = CreateContext(options, 202, "tenantId"))
        {
            var classes = await tenant202.Classes.Select(x => x.Name).ToListAsync();
            classes.Should().Equal("Tenant 202");
        }

        await using (var noTenant = CreateContext(options))
        {
            (await noTenant.Classes.CountAsync()).Should().Be(0);
            (await noTenant.TenantSettings.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task Generic_repository_cannot_read_another_tenants_record()
    {
        var options = CreateOptions();
        long otherTenantClassId;

        await using (var seed = CreateContext(options))
        {
            var item = new Class { Name = "Private", NumericValue = 1, TenantId = 202 };
            seed.Classes.Add(item);
            await seed.SaveChangesAsync();
            otherTenantClassId = item.Id;
        }

        await using var tenant101 = CreateContext(options, 101);
        var repository = new GenericRepository<Class>(tenant101);

        var result = await repository.GetByIdAsync(otherTenantClassId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Authenticated_tenant_cannot_write_to_another_tenant()
    {
        var options = CreateOptions();
        await using var tenant101 = CreateContext(options, 101);

        tenant101.Classes.Add(
            new Class { Name = "Wrong tenant", NumericValue = 1, TenantId = 202 });

        var action = () => tenant101.SaveChangesAsync();

        await action.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Tenant boundary violation*");
    }

    [Fact]
    public async Task New_record_inherits_authenticated_tenant_when_not_supplied()
    {
        var options = CreateOptions();
        await using var tenant101 = CreateContext(options, 101);
        var item = new Class { Name = "Current tenant", NumericValue = 1 };

        tenant101.Classes.Add(item);
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
        long? tenantId = null,
        string tenantClaimType = "TenantId")
    {
        var httpContext = new DefaultHttpContext();

        if (tenantId.HasValue)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9001"),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim(tenantClaimType, tenantId.Value.ToString())
            ], "TestAuthentication"));
        }

        return new EduOSDbContext(
            options,
            new HttpContextAccessor { HttpContext = httpContext });
    }
}
