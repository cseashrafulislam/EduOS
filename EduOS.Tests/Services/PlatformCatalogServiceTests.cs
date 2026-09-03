using EduOS.Core.Entities.SaaS;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Seed;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public class PlatformCatalogServiceTests
{
    [Fact]
    public async Task Public_catalog_returns_ordered_active_presets_and_modules()
    {
        await using var context = CreateContext();
        await PlatformCatalogSeeder.SeedAsync(context);
        var service = CreateService(context);

        var institutionTypes = await service.GetInstitutionTypesAsync();
        var modules = await service.GetModulesAsync();

        institutionTypes.Success.Should().BeTrue();
        institutionTypes.Data.Should().HaveCount(13);
        institutionTypes.Data!.Select(x => x.DisplayOrder).Should().BeInAscendingOrder();
        modules.Success.Should().BeTrue();
        modules.Data.Should().HaveCount(20);
        modules.Data!.Select(x => x.DisplayOrder).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Institution_type_lookup_normalizes_code_and_returns_safe_structured_metadata()
    {
        await using var context = CreateContext();
        await PlatformCatalogSeeder.SeedAsync(context);
        var service = CreateService(context);

        var result = await service.GetInstitutionTypeByCodeAsync(" university ");

        result.Success.Should().BeTrue();
        result.Data!.Code.Should().Be("UNIVERSITY");
        result.Data.AcademicCycleType.Should().Be("Semester");
        result.Data.Terminology.Should().Contain("program", "Program");
        result.Data.DefaultSettings.Should().Contain("currency", "BDT");
        result.Data.Modules.Should().HaveCount(20);
        result.Data.Modules.Should().Contain(x => x.Code == "LMS" && x.IsEnabledByDefault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../secret")]
    [InlineData("not a code")]
    public async Task Institution_type_lookup_rejects_malformed_codes(string code)
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.GetInstitutionTypeByCodeAsync(code);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Public_catalog_hides_disabled_presets_and_modules()
    {
        await using var context = CreateContext();
        await PlatformCatalogSeeder.SeedAsync(context);
        var privateType = await context.InstitutionTypeDefinitions.SingleAsync(x => x.Code == "PRIVATE_TUTOR");
        var disabledModule = await context.ProductModules.SingleAsync(x => x.Code == "AI_INSIGHTS");
        privateType.IsPubliclyVisible = false;
        disabledModule.IsActive = false;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var institutionTypes = await service.GetInstitutionTypesAsync();
        var modules = await service.GetModulesAsync();

        institutionTypes.Data.Should().NotContain(x => x.Code == "PRIVATE_TUTOR");
        modules.Data.Should().NotContain(x => x.Code == "AI_INSIGHTS");
    }

    private static PlatformCatalogService CreateService(EduOSDbContext context)
    {
        return new PlatformCatalogService(
            new GenericRepository<InstitutionTypeDefinition>(context),
            new GenericRepository<ProductModule>(context),
            NullLogger<PlatformCatalogService>.Instance);
    }

    private static EduOSDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"platform-catalog-service-{Guid.NewGuid():N}")
            .Options;
        return new EduOSDbContext(options);
    }
}
