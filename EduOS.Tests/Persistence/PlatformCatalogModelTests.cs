using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public class PlatformCatalogModelTests
{
    [Fact]
    public void Catalog_codes_and_preset_module_pair_have_unique_indexes()
    {
        using var context = CreateContext();

        var institutionType = context.Model.FindEntityType(typeof(InstitutionTypeDefinition))!;
        var module = context.Model.FindEntityType(typeof(ProductModule))!;
        var mapping = context.Model.FindEntityType(typeof(InstitutionTypeModule))!;

        institutionType.GetIndexes().Should().Contain(index =>
            index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(new[] { "Code" }));
        module.GetIndexes().Should().Contain(index =>
            index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(new[] { "Code" }));
        mapping.GetIndexes().Should().Contain(index =>
            index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(
                new[] { "InstitutionTypeDefinitionId", "ProductModuleId" }));
    }

    [Fact]
    public void Tenant_preset_foreign_key_is_optional_and_restricts_delete()
    {
        using var context = CreateContext();
        var tenant = context.Model.FindEntityType(typeof(Tenant))!;

        var foreignKey = tenant.GetForeignKeys().Single(x =>
            x.PrincipalEntityType.ClrType == typeof(InstitutionTypeDefinition));

        foreignKey.IsRequired.Should().BeFalse();
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    private static EduOSDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"platform-catalog-model-{Guid.NewGuid():N}")
            .Options;
        return new EduOSDbContext(options);
    }
}
