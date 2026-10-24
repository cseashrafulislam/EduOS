using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.SaaS;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PlatformModuleFeatureMappingTests
{
    [Fact]
    public async Task Host_can_map_unmap_restore_and_replay_idempotently()
    {
        await using var db = Context();
        var module = new ProductModule { Code = "TEST_MODULE", Name = "Test module", RowVersion = [1, 2, 3] };
        var feature = new Feature { Code = "TEST_FEATURE", Name = "Test feature", RowVersion = [4, 5, 6] };
        db.AddRange(module, feature);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var create = Request(module, feature, true);
        var first = await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id, create);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.IsMapped.Should().BeTrue();
        (await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id, create)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "ProductModuleFeature.MappingChanged")).Should().Be(1);

        var directory = (await service.GetAdminModuleFeaturesAsync(module.Id)).Data!;
        var mapped = directory.Features.Single(x => x.FeatureId == feature.Id);
        mapped.IsMapped.Should().BeTrue();
        var remove = Request(module, feature, false);
        remove.MappingRowVersion = mapped.MappingRowVersion;
        var removed = await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id, remove);
        removed.Success.Should().BeTrue(removed.Message);
        removed.Data!.IsMapped.Should().BeFalse();
        (await db.ProductModuleFeatures.IgnoreQueryFilters().SingleAsync()).IsDeleted.Should().BeTrue();

        var restore = Request(module, feature, true);
        restore.MappingRowVersion = removed.Data.MappingRowVersion;
        var restored = await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id, restore);
        restored.Success.Should().BeTrue(restored.Message);
        restored.Data!.IsMapped.Should().BeTrue();
        (await db.ProductModuleFeatures.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "ProductModuleFeature.MappingChanged")).Should().Be(3);
        (await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id, remove)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Tenant_admin_cannot_read_or_change_host_mappings()
    {
        await using var db = Context();
        var module = new ProductModule { Code = "TEST_MODULE", Name = "Test" };
        var feature = new Feature { Code = "TEST_FEATURE", Name = "Test" };
        db.AddRange(module, feature);
        await db.SaveChangesAsync();
        var service = Service(db, new User(false));
        (await service.GetAdminModuleFeaturesAsync(module.Id)).StatusCode.Should().Be(403);
        (await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id,
            Request(module, feature, true))).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Stale_catalog_and_inactive_feature_are_rejected()
    {
        await using var db = Context();
        var module = new ProductModule { Code = "TEST_MODULE", Name = "Test", RowVersion = [1, 2, 3] };
        var feature = new Feature { Code = "TEST_FEATURE", Name = "Test", RowVersion = [4, 5, 6] };
        db.AddRange(module, feature);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var stale = Request(module, feature, true);
        stale.ModuleRowVersion = "CQkJ";
        (await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id, stale)).StatusCode.Should().Be(409);
        feature.IsActive = false;
        await db.SaveChangesAsync();
        (await service.SaveModuleFeatureMappingAsync(module.Id, feature.Id,
            Request(module, feature, true))).StatusCode.Should().Be(409);
        (await db.ProductModuleFeatures.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Removing_a_mapping_used_by_an_existing_tenant_is_blocked()
    {
        await using var db = Context();
        var module = new ProductModule { Code = "TEST_MODULE", Name = "Test", RowVersion = [1, 2, 3] };
        var feature = new Feature { Code = "TEST_FEATURE", Name = "Test", RowVersion = [4, 5, 6] };
        var link = new ProductModuleFeature { ProductModuleId = 0, FeatureId = 0, RowVersion = [7, 8, 9] };
        db.AddRange(module, feature);
        await db.SaveChangesAsync();
        link.ProductModuleId = module.Id;
        link.FeatureId = feature.Id;
        db.ProductModuleFeatures.Add(link);
        db.TenantModules.Add(new TenantModule { TenantId = 101, ProductModuleId = module.Id, IsEnabled = true });
        await db.SaveChangesAsync();
        var request = Request(module, feature, false);
        request.MappingRowVersion = Convert.ToBase64String(link.RowVersion);
        (await Service(db, new User(true)).SaveModuleFeatureMappingAsync(module.Id, feature.Id, request))
            .StatusCode.Should().Be(409);
        (await db.ProductModuleFeatures.SingleAsync()).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Existing_institution_preset_blocks_mapping_removal()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition { Code = "TEST_TYPE", Name = "Test" };
        var module = new ProductModule { Code = "TEST_MODULE", Name = "Test", RowVersion = [1, 2, 3] };
        var feature = new Feature { Code = "TEST_FEATURE", Name = "Test", RowVersion = [4, 5, 6] };
        db.AddRange(type, module, feature);
        await db.SaveChangesAsync();
        var link = new ProductModuleFeature
        {
            ProductModuleId = module.Id, FeatureId = feature.Id, RowVersion = [7, 8, 9]
        };
        db.ProductModuleFeatures.Add(link);
        db.InstitutionTypeModules.Add(new InstitutionTypeModule
        {
            InstitutionTypeDefinitionId = type.Id, ProductModuleId = module.Id,
            IsDefaultEnabled = true
        });
        db.Tenants.Add(new Tenant
        {
            Code = "EXISTING", Name = "Existing", Email = "existing@example.test",
            InstitutionTypeDefinitionId = type.Id
        });
        await db.SaveChangesAsync();
        var request = Request(module, feature, false);
        request.MappingRowVersion = Convert.ToBase64String(link.RowVersion);
        (await Service(db, new User(true)).SaveModuleFeatureMappingAsync(module.Id, feature.Id, request))
            .StatusCode.Should().Be(409);
        link.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Core_module_mapping_cannot_be_removed()
    {
        await using var db = Context();
        var module = new ProductModule
        {
            Code = "CORE_ADMIN", Name = "Core", IsCore = true, RowVersion = [1, 2, 3]
        };
        var feature = new Feature { Code = "CORE_FEATURE", Name = "Core", RowVersion = [4, 5, 6] };
        db.AddRange(module, feature);
        await db.SaveChangesAsync();
        var link = new ProductModuleFeature
        {
            ProductModuleId = module.Id, FeatureId = feature.Id, RowVersion = [7, 8, 9]
        };
        db.ProductModuleFeatures.Add(link);
        await db.SaveChangesAsync();
        var request = Request(module, feature, false);
        request.MappingRowVersion = Convert.ToBase64String(link.RowVersion);
        (await Service(db, new User(true)).SaveModuleFeatureMappingAsync(module.Id, feature.Id, request))
            .StatusCode.Should().Be(409);
    }

    private static SavePlatformModuleFeatureRequestDto Request(
        ProductModule module, Feature feature, bool mapped) => new()
    {
        RequestId = Guid.NewGuid(),
        ModuleRowVersion = Convert.ToBase64String(module.RowVersion),
        FeatureRowVersion = Convert.ToBase64String(feature.RowVersion),
        IsMapped = mapped
    };

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("platform-module-features-" + Guid.NewGuid().ToString("N")).Options);

    private static PlatformCatalogService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<InstitutionTypeDefinition>(db),
        new GenericRepository<InstitutionTypeModule>(db),
        new GenericRepository<ProductModule>(db),
        NullLogger<PlatformCatalogService>.Instance, db, user);

    private sealed class User(bool superAdmin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 50;
        public long TenantId => superAdmin ? 0 : 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => superAdmin;
        public bool IsTenantAdmin => !superAdmin;
        public IReadOnlyList<string> Roles => superAdmin ? ["SuperAdmin"] : ["TenantAdmin"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
