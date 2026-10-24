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

public sealed class PlatformFeatureAdministrationTests
{
    [Fact]
    public async Task Superadmin_updates_feature_and_idempotent_retry_preserves_single_audit()
    {
        await using var db = Context();
        var feature = new Feature { Code = "TEST_FEATURE", Name = "Original", IsActive = true, RowVersion = [1, 2, 3] };
        db.Features.Add(feature);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var request = new SavePlatformFeatureRequestDto
        {
            RequestId = Guid.NewGuid(), RowVersion = Convert.ToBase64String(feature.RowVersion),
            Name = " Updated ", Description = "  New description ", IsActive = false
        };
        var result = await service.SaveFeatureConfigurationAsync(feature.Id, request);
        result.Success.Should().BeTrue(result.Message);
        result.Data!.Name.Should().Be("Updated");
        result.Data.IsActive.Should().BeFalse();
        (await service.SaveFeatureConfigurationAsync(feature.Id, request)).Success.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "Feature.ConfigurationChanged")).Should().Be(1);
        feature.Code.Should().Be("TEST_FEATURE");
        (await service.GetAdminFeaturesAsync()).Data.Should().ContainSingle(x => x.Id == feature.Id);
    }

    [Fact]
    public async Task Tenant_admin_cannot_list_or_change_host_features()
    {
        await using var db = Context();
        var service = Service(db, new User(false));
        (await service.GetAdminFeaturesAsync()).StatusCode.Should().Be(403);
        (await service.SaveFeatureConfigurationAsync(1, new SavePlatformFeatureRequestDto
        {
            RequestId = Guid.NewGuid(), RowVersion = "AQID", Name = "Forbidden", IsActive = false
        })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Stale_rowversion_and_invalid_metadata_cannot_modify_feature()
    {
        await using var db = Context();
        var feature = new Feature { Code = "TEST_FEATURE", Name = "Original", IsActive = true, RowVersion = [1, 2, 3] };
        db.Features.Add(feature);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var stale = await service.SaveFeatureConfigurationAsync(feature.Id, new SavePlatformFeatureRequestDto
        {
            RequestId = Guid.NewGuid(), RowVersion = "CQkJ", Name = "Changed", IsActive = false
        });
        stale.StatusCode.Should().Be(409);
        var invalid = await service.SaveFeatureConfigurationAsync(feature.Id, new SavePlatformFeatureRequestDto
        {
            RequestId = Guid.NewGuid(), RowVersion = "AQID", Name = new string('x', 151)
        });
        invalid.StatusCode.Should().Be(400);
        feature.Name.Should().Be("Original");
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "Feature.ConfigurationChanged")).Should().Be(0);
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("platform-feature-admin-" + Guid.NewGuid().ToString("N")).Options);

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
