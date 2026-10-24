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

public sealed class PlatformProductModuleAdministrationTests
{
    [Fact]
    public async Task Superadmin_updates_optional_module_and_retry_creates_one_audit()
    {
        await using var db = Context();
        var module = new ProductModule
        {
            Code = "OPTIONAL_TEST", Name = "Old", IsActive = true,
            DisplayOrder = 100, RowVersion = [1, 2, 3]
        };
        db.ProductModules.Add(module);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var request = Request(module, false);
        var changed = await service.SaveProductModuleConfigurationAsync(module.Id, request);
        changed.Success.Should().BeTrue(changed.Message);
        changed.Data!.Name.Should().Be("Updated");
        changed.Data.IsActive.Should().BeFalse();
        (await service.SaveProductModuleConfigurationAsync(module.Id, request)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "ProductModule.ConfigurationChanged")).Should().Be(1);
        module.Code.Should().Be("OPTIONAL_TEST");
        (await service.GetModulesAsync()).Data.Should().NotContain(x => x.Code == module.Code);
    }

    [Fact]
    public async Task Tenant_admin_cannot_read_or_modify_global_modules()
    {
        await using var db = Context();
        var service = Service(db, new User(false));
        (await service.GetAdminProductModulesAsync()).StatusCode.Should().Be(403);
        (await service.SaveProductModuleConfigurationAsync(1, new SavePlatformProductModuleRequestDto
        {
            RequestId = Guid.NewGuid(), RowVersion = "AQID", Name = "Denied"
        })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Core_module_cannot_be_deactivated()
    {
        await using var db = Context();
        var module = new ProductModule { Code = "CORE_ADMIN", Name = "Core", IsCore = true, RowVersion = [1, 2, 3] };
        db.ProductModules.Add(module);
        await db.SaveChangesAsync();
        var response = await Service(db, new User(true)).SaveProductModuleConfigurationAsync(
            module.Id, Request(module, false));
        response.StatusCode.Should().Be(409);
        module.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Preset_or_enabled_tenant_module_blocks_deactivation()
    {
        await using var db = Context();
        var module = new ProductModule { Code = "OPTIONAL_TEST", Name = "Optional", RowVersion = [1, 2, 3] };
        db.ProductModules.Add(module);
        await db.SaveChangesAsync();
        db.InstitutionTypeModules.Add(new InstitutionTypeModule
        {
            ProductModuleId = module.Id, InstitutionTypeDefinitionId = 5,
            IsDefaultEnabled = true
        });
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        (await service.SaveProductModuleConfigurationAsync(module.Id, Request(module, false))).StatusCode.Should().Be(409);
        db.InstitutionTypeModules.RemoveRange(db.InstitutionTypeModules);
        await db.SaveChangesAsync();
        db.TenantModules.Add(new TenantModule { TenantId = 7, ProductModuleId = module.Id, IsEnabled = true });
        await db.SaveChangesAsync();
        (await service.SaveProductModuleConfigurationAsync(module.Id, Request(module, false))).StatusCode.Should().Be(409);
        module.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Stale_row_version_and_invalid_metadata_do_not_change_module()
    {
        await using var db = Context();
        var module = new ProductModule { Code = "OPTIONAL_TEST", Name = "Original", RowVersion = [1, 2, 3] };
        db.ProductModules.Add(module);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var stale = Request(module, false);
        stale.RowVersion = "CQkJ";
        (await service.SaveProductModuleConfigurationAsync(module.Id, stale)).StatusCode.Should().Be(409);
        var invalid = Request(module, false);
        invalid.Name = new string('x', 151);
        (await service.SaveProductModuleConfigurationAsync(module.Id, invalid)).StatusCode.Should().Be(400);
        module.Name.Should().Be("Original");
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "ProductModule.ConfigurationChanged")).Should().Be(0);
    }

    private static SavePlatformProductModuleRequestDto Request(ProductModule module, bool active) => new()
    {
        RequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(module.RowVersion),
        Name = " Updated ", Description = " Description ", IsActive = active, DisplayOrder = 25
    };

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("platform-product-module-admin-" + Guid.NewGuid().ToString("N")).Options);

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
