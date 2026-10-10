using EduOS.Core.Entities.SaaS;
using EduOS.Persistence.Context;
using EduOS.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class PlatformCatalogSeederTests
{
    [Fact]
    public async Task Seeder_is_idempotent_and_presets_reference_canonical_module_ids()
    {
        await using var db = Context();
        await SubscriptionSeeder.SeedAsync(db);
        await PlatformCatalogSeeder.SeedAsync(db);
        await PlatformCatalogSeeder.SeedAsync(db);
        (await db.InstitutionTypeDefinitions.CountAsync()).Should().Be(13);
        (await db.ProductModules.CountAsync()).Should().Be(20);
        (await db.InstitutionTypeModules.CountAsync()).Should().Be(212);
        (await db.ProductModuleFeatures.CountAsync()).Should().Be(31);
        var university = await db.InstitutionTypeDefinitions.SingleAsync(x => x.Code == "UNIVERSITY");
        var modules = await (from selected in db.InstitutionTypeModules
            join module in db.ProductModules on selected.ProductModuleId equals module.Id
            where selected.InstitutionTypeDefinitionId == university.Id
            select new { module.Code, selected.IsRequired }).ToListAsync();
        modules.Should().HaveCount(20);
        modules.Select(x => x.Code).Should().Contain("LMS");
        modules.Where(x => x.IsRequired).Select(x => x.Code)
            .Should().BeEquivalentTo("CORE_ADMIN", "STUDENT", "ACADEMIC");
    }

    [Fact]
    public async Task Subscription_seeder_preserves_custom_existing_names_and_backfills_blank_descriptions()
    {
        await using var db = Context();
        await SubscriptionSeeder.SeedAsync(db);
        var feature = await db.Features.SingleAsync(x => x.Code == "STUDENT_MGMT");
        var plan = await db.SubscriptionPlans.SingleAsync(x => x.Code == "BASIC");
        feature.Name = "Custom Student Label";
        feature.Description = "";
        plan.Name = "Custom Basic Plan";
        await db.SaveChangesAsync();
        await SubscriptionSeeder.SeedAsync(db);
        (await db.Features.CountAsync()).Should().Be(29);
        (await db.SubscriptionPlans.CountAsync()).Should().Be(4);
        feature.Name.Should().Be("Custom Student Label");
        feature.Description.Should().NotBeNullOrWhiteSpace();
        plan.Name.Should().Be("Custom Basic Plan");
    }

    [Fact]
    public async Task Platform_catalog_seeding_does_not_infer_or_mutate_existing_tenant_types()
    {
        await using var db = Context();
        var tenant = new Tenant { Name = "Custom", Code = "CUSTOM-01",
            Email = "custom@example.test", InstitutionTypeDefinitionId = null };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        await PlatformCatalogSeeder.SeedAsync(db);
        tenant.InstitutionTypeDefinitionId.Should().BeNull();
        (await db.InstitutionTypeDefinitions.CountAsync()).Should().Be(13);
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("platform-catalog-" + Guid.NewGuid().ToString("N")).Options);
}
