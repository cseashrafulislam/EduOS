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

public sealed class PlatformCatalogActivationTests
{
    [Fact]
    public async Task Superadmin_can_hide_signup_type_and_retry_without_duplicate_audit()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition
        {
            Code = "TEST_TYPE", Name = "Test Institution", IsActive = true,
            RowVersion = new byte[] { 1, 2, 3 }
        };
        db.InstitutionTypeDefinitions.Add(type);
        await db.SaveChangesAsync();
        var service = Service(db, new User(true));
        var request = new ChangeInstitutionTypeActivationRequestDto
        {
            RequestId = Guid.NewGuid(), IsActive = false,
            RowVersion = Convert.ToBase64String(type.RowVersion)
        };
        var changed = await service.ChangeInstitutionTypeActivationAsync(type.Id, request);
        changed.Success.Should().BeTrue(changed.Message);
        changed.Data!.IsActive.Should().BeFalse();
        var retry = await service.ChangeInstitutionTypeActivationAsync(type.Id, request);
        retry.Success.Should().BeTrue(retry.Message);
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "InstitutionType.ActivationChanged")).Should().Be(1);
        var publicCatalog = await service.GetInstitutionTypesAsync();
        publicCatalog.Data.Should().NotContain(x => x.Code == "TEST_TYPE");
    }

    [Fact]
    public async Task Tenant_admin_cannot_read_or_modify_host_catalog()
    {
        await using var db = Context();
        var service = Service(db, new User(false));
        (await service.GetAdminInstitutionTypesAsync()).StatusCode.Should().Be(403);
        (await service.ChangeInstitutionTypeActivationAsync(1,
            new ChangeInstitutionTypeActivationRequestDto
            {
                RequestId = Guid.NewGuid(), IsActive = false, RowVersion = "AQID"
            })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Stale_row_version_rejected_without_audit()
    {
        await using var db = Context();
        var type = new InstitutionTypeDefinition
        {
            Code = "TEST_TYPE", Name = "Test Institution", IsActive = true,
            RowVersion = new byte[] { 1, 2, 3 }
        };
        db.InstitutionTypeDefinitions.Add(type);
        await db.SaveChangesAsync();
        var result = await Service(db, new User(true)).ChangeInstitutionTypeActivationAsync(type.Id,
            new ChangeInstitutionTypeActivationRequestDto
            {
                RequestId = Guid.NewGuid(), IsActive = false, RowVersion = "CQkJ"
            });
        result.StatusCode.Should().Be(409);
        type.IsActive.Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(x =>
            x.Action == "InstitutionType.ActivationChanged")).Should().Be(0);
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("platform-catalog-activation-" + Guid.NewGuid().ToString("N")).Options);

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
