using EduOS.Core.Entities.Tenants;
using EduOS.Persistence.Context;
using EduOS.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public class PlatformCatalogSeederTests
{
    [Fact]
    public async Task Seeder_is_idempotent_and_creates_complete_catalog()
    {
        await using var context = CreateContext();

        await SubscriptionSeeder.SeedAsync(context);
        await PlatformCatalogSeeder.SeedAsync(context);
        await PlatformCatalogSeeder.SeedAsync(context);

        (await context.InstitutionTypeDefinitions.CountAsync()).Should().Be(13);
        (await context.ProductModules.CountAsync()).Should().Be(20);
        (await context.InstitutionTypeModules.CountAsync()).Should().Be(212);
        (await context.ProductModuleFeatures.CountAsync()).Should().Be(31);

        var university = await context.InstitutionTypeDefinitions
            .Include(x => x.Modules)
            .ThenInclude(x => x.ProductModule)
            .SingleAsync(x => x.Code == "UNIVERSITY");

        university.Modules.Should().HaveCount(20);
        university.Modules.Should().Contain(x => x.ProductModule!.Code == "LMS");
        university.Modules.Where(x => x.IsRequired)
            .Select(x => x.ProductModule!.Code)
            .Should().BeEquivalentTo("CORE_ADMIN", "STUDENT", "ACADEMIC");
    }

    [Fact]
    public async Task Seeder_backfills_recognized_legacy_tenant_type_without_changing_unknown_values()
    {
        await using var context = CreateContext();
        var recognized = Tenant("Recognized", "university");
        var custom = Tenant("Custom", "SPECIAL_INSTITUTE");
        context.Tenants.AddRange(recognized, custom);
        await context.SaveChangesAsync();

        await PlatformCatalogSeeder.SeedAsync(context);

        recognized.InstitutionType.Should().Be("UNIVERSITY");
        recognized.InstitutionTypeDefinitionId.Should().NotBeNull();
        custom.InstitutionType.Should().Be("SPECIAL_INSTITUTE");
        custom.InstitutionTypeDefinitionId.Should().BeNull();
    }

    private static EduOSDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"platform-catalog-seed-{Guid.NewGuid():N}")
            .Options;
        return new EduOSDbContext(options);
    }

    private static Tenant Tenant(string name, string institutionType)
    {
        return new Tenant
        {
            Name = name,
            Code = $"{name.ToUpperInvariant()}-01",
            Email = $"{name.ToLowerInvariant()}@example.test",
            OwnerName = "Owner",
            InstitutionType = institutionType
        };
    }
}
