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

public sealed class PlatformInstitutionModulePresetTests
{
    [Fact]
    public async Task Host_can_create_and_update_optional_preset_with_idempotent_audit()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition { Code = "TEST", Name = "Test", RowVersion = [1, 2, 3] };
        var module = new ProductModule { Code = "OPTIONAL", Name = "Optional", RowVersion = [1, 2, 3] };
        db.AddRange(type, module);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var request = Request(type, true, false);
        var first = await service.SaveInstitutionModulePresetAsync(type.Id, module.Id, request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.IsDefaultEnabled.Should().BeTrue();
        (await service.SaveInstitutionModulePresetAsync(type.Id, module.Id, request)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "InstitutionTypeModule.PresetChanged")).Should().Be(1);
        var saved = (await service.GetAdminInstitutionModulePresetAsync(type.Id)).Data!;
        saved.Modules.Single(x => x.ModuleId == module.Id).IsDefaultEnabled.Should().BeTrue();
        var update = Request(type, false, false);
        update.RowVersion = saved.Modules.Single(x => x.ModuleId == module.Id).RowVersion;
        (await service.SaveInstitutionModulePresetAsync(type.Id, module.Id, update)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "InstitutionTypeModule.PresetChanged")).Should().Be(2);
    }

    [Fact]
    public async Task Tenant_admin_is_denied_and_core_cannot_be_disabled()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition { Code = "TEST", Name = "Test", RowVersion = [1, 2, 3] };
        var core = new ProductModule { Code = "CORE_ADMIN", Name = "Core", IsCore = true };
        db.AddRange(type, core);
        await db.SaveChangesAsync();
        var tenant = Service(db, new User(false));
        (await tenant.GetAdminInstitutionModulePresetAsync(type.Id)).StatusCode.Should().Be(403);
        (await tenant.SaveInstitutionModulePresetAsync(type.Id, core.Id, Request(type, false, false))).StatusCode.Should().Be(403);
        (await Service(db, new User(true)).SaveInstitutionModulePresetAsync(type.Id, core.Id, Request(type, false, false))).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Stale_version_and_retroactive_required_preset_are_rejected()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition { Code = "TEST", Name = "Test", RowVersion = [1, 2, 3] };
        var module = new ProductModule { Code = "OPTIONAL", Name = "Optional" };
        db.AddRange(type, module);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var stale = Request(type, true, false);
        stale.InstitutionTypeRowVersion = "CQkJ";
        (await service.SaveInstitutionModulePresetAsync(type.Id, module.Id, stale)).StatusCode.Should().Be(409);
        db.Tenants.Add(new Tenant { Code = "T_TEST", Name = "Existing", InstitutionTypeDefinitionId = type.Id });
        await db.SaveChangesAsync();
        (await service.SaveInstitutionModulePresetAsync(type.Id, module.Id, Request(type, true, true))).StatusCode.Should().Be(409);
        (await db.InstitutionTypeModules.CountAsync()).Should().Be(0);
    }

    private static SavePlatformInstitutionModulePresetDto Request(
        InstitutionTypeDefinition type, bool enabled, bool required) => new()
    {
        RequestId = Guid.NewGuid(), InstitutionTypeRowVersion = Convert.ToBase64String(type.RowVersion),
        IsDefaultEnabled = enabled, IsRequired = required
    };

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("platform-presets-" + Guid.NewGuid().ToString("N")).Options);

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
